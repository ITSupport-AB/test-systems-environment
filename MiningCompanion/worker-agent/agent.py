#!/usr/bin/env python3
"""Worker-side bridge for a user-owned mining rig.

This process does not mine. It polls the gateway for commands and reports
telemetry. Commands are configured as argv arrays and never pass through a
shell.
"""

import json
import os
import signal
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from urllib.parse import urlparse


class AgentError(Exception):
    pass


class WorkerAgent:
    def __init__(self, config):
        self.config = dict(config)
        self.config.setdefault("pool_user", self.config.get("payout_address", ""))
        self.config.setdefault("pool_password", "x")
        self.running = True
        self.started_at = time.monotonic()

    def request(self, method, path, payload=None):
        body = None if payload is None else json.dumps(payload).encode("utf-8")
        request = urllib.request.Request(
            self.config["gateway_url"].rstrip("/") + path,
            data=body,
            method=method,
            headers={
                "Accept": "application/json",
                "Content-Type": "application/json",
                "X-Worker-Key": self.config["worker_key"],
            },
        )
        try:
            with urllib.request.urlopen(request, timeout=15) as response:
                content = response.read().decode("utf-8")
                return json.loads(content) if content else None
        except urllib.error.HTTPError as error:
            raise AgentError(f"gateway returned HTTP {error.code}") from error
        except urllib.error.URLError as error:
            raise AgentError(f"gateway connection failed: {error.reason}") from error

    def telemetry(self):
        status_command = self.config.get("telemetry_command")
        if not isinstance(status_command, list) or not status_command or not all(isinstance(value, str) for value in status_command):
            raise AgentError("telemetry_command must be an argv list returning JSON status")
        result = subprocess.run(
            status_command,
            check=True,
            shell=False,
            timeout=15,
            capture_output=True,
            text=True,
        )
        telemetry = json.loads(result.stdout)
        return {
            "coin": str(telemetry.get("coin", self.config["coin"])),
            "network": str(telemetry.get("network", self.config["network"])),
            "algorithm": str(telemetry.get("algorithm", self.config["algorithm"])),
            "state": str(telemetry.get("state", "UNKNOWN")).upper(),
            "hashrate": str(telemetry.get("hashrate", "--")),
            "temperatureCelsius": float(telemetry.get("temperature_celsius", 0)),
            "uptime": str(telemetry.get("uptime", int(time.monotonic() - self.started_at))),
        }

    def heartbeat(self):
        self.request("POST", f"/v1/agent/workers/{self.config['worker_id']}/heartbeat", self.telemetry())

    def poll_commands(self):
        result = self.request("GET", f"/v1/agent/workers/{self.config['worker_id']}/commands") or {}
        for command in result.get("commands", []):
            self.apply_command(command["command"])

    def apply_command(self, command):
        argv = self.config.get("commands", {}).get(command)
        if not isinstance(argv, list) or not argv or not all(isinstance(value, str) for value in argv):
            raise AgentError(f"no argv command configured for {command}")
        variables = {
            "{coin}": self.config["coin"],
            "{network}": self.config["network"],
            "{pool_url}": self.config["pool_url"],
            "{pool_user}": self.config["pool_user"],
            "{pool_password}": self.config["pool_password"],
            "{worker_name}": self.config["worker_name"],
            "{payout_address}": self.config["payout_address"],
            "{algorithm}": self.config["algorithm"],
        }
        expanded = [variables.get(value, value) for value in argv]
        subprocess.run(expanded, check=True, shell=False, timeout=120)
        self.config["worker_state"] = "RUNNING" if command in ("START", "RESTART") else "STOPPED"

    def run(self):
        interval = max(10, int(self.config.get("poll_interval_seconds", 30)))
        while self.running:
            try:
                self.poll_commands()
                self.heartbeat()
            except (AgentError, OSError, subprocess.SubprocessError) as error:
                print(f"worker-agent: {error}", file=sys.stderr)
            time.sleep(interval)


def load_config(path):
    config = json.loads(Path(path).read_text(encoding="utf-8"))
    required = ("gateway_url", "worker_id", "worker_key")
    missing = [key for key in required if not config.get(key)]
    if missing:
        raise AgentError(f"missing configuration: {', '.join(missing)}")
    if not config["gateway_url"].lower().startswith("https://"):
        raise AgentError("gateway_url must use HTTPS")
    for key in ("coin", "network", "payout_address", "algorithm", "worker_name", "telemetry_command"):
        if not isinstance(config.get(key), str) or not config[key].strip():
            if key == "telemetry_command":
                if not isinstance(config.get(key), list) or not config[key] or not all(isinstance(value, str) for value in config[key]):
                    raise AgentError("telemetry_command must be an argv list")
            else:
                raise AgentError(f"missing configuration: {key}")
    if config.get("work_source", "pool") != "pool":
        raise AgentError("work_source must be pool; solo mining requires a full node and stratum bridge")
    config.setdefault("pool_user", config["payout_address"])
    config.setdefault("pool_password", "x")
    if urlparse(config["pool_url"]).scheme not in ("stratum+tcp", "stratum+ssl"):
        raise AgentError("pool_url must use stratum+tcp or stratum+ssl")
    return config


def main():
    agent = WorkerAgent(load_config(os.environ.get("MINING_AGENT_CONFIG", "config.json")))
    signal.signal(signal.SIGTERM, lambda *_: setattr(agent, "running", False))
    signal.signal(signal.SIGINT, lambda *_: setattr(agent, "running", False))
    agent.run()


if __name__ == "__main__":
    main()