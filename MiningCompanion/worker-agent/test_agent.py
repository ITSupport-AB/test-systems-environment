import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import agent


def valid_config():
    return {
        "gateway_url": "https://gateway.example.test",
        "worker_id": "rig-01",
        "worker_key": "worker-secret",
        "coin": "monero",
        "network": "mainnet",
        "work_source": "pool",
        "pool_url": "stratum+ssl://pool.example.test:443",
        "payout_address": "wallet-address",
        "algorithm": "randomx",
        "worker_name": "rig-01",
        "telemetry_command": ["minerctl", "status", "--json"],
        "commands": {"START": ["minerctl", "start"]},
    }


class WorkerAgentTests(unittest.TestCase):
    def test_load_config_requires_https_and_pool_mode(self):
        config = valid_config()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "config.json"
            path.write_text(json.dumps(config), encoding="utf-8")
            self.assertEqual(agent.load_config(path)["coin"], "monero")

            config["gateway_url"] = "http://gateway.example.test"
            path.write_text(json.dumps(config), encoding="utf-8")
            with self.assertRaises(agent.AgentError):
                agent.load_config(path)

    def test_solo_mode_is_rejected(self):
        config = valid_config()
        config["work_source"] = "solo"
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "config.json"
            path.write_text(json.dumps(config), encoding="utf-8")
            with self.assertRaisesRegex(agent.AgentError, "solo mining"):
                agent.load_config(path)

    @patch("agent.subprocess.run")
    def test_telemetry_comes_from_miner_json(self, run):
        run.return_value.stdout = json.dumps({
            "state": "RUNNING",
            "hashrate": "125 MH/s",
            "temperature_celsius": 63,
            "uptime": "01:02:03",
        })
        worker = agent.WorkerAgent(valid_config())
        self.assertEqual(worker.telemetry()["hashrate"], "125 MH/s")
        run.assert_called_once_with(
            ["minerctl", "status", "--json"],
            check=True,
            shell=False,
            timeout=15,
            capture_output=True,
            text=True,
        )

    @patch("agent.subprocess.run")
    def test_commands_are_expanded_without_shell(self, run):
        config = valid_config()
        config["commands"]["START"] = ["minerctl", "start", "--wallet", "{payout_address}"]
        worker = agent.WorkerAgent(config)
        worker.apply_command("START")
        run.assert_called_once_with(
            ["minerctl", "start", "--wallet", "wallet-address"],
            check=True,
            shell=False,
            timeout=120,
        )


if __name__ == "__main__":
    unittest.main()