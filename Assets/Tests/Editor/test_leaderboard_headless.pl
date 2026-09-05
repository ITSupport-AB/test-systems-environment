#!/usr/bin/perl
# ============================================================================
# test_leaderboard_headless.pl - Headless verification of leaderboard logic.
#
# What this tests:
#   1. Firestore rules enforce score >= current score on update
#   2. Firestore rules require auth, enforce field whitelist, prevent deletes
#   3. LeaderboardManager.cs client-side anti-inflation checks
#   4. Cache key generation and invalidation logic
#
# Run: perl Assets/Tests/Editor/test_leaderboard_headless.pl
# ============================================================================

use strict;
use warnings;

sub read_file {
    my ($path) = @_;
    open my $fh, '<', $path or return undef;
    local $/;
    my $content = <$fh>;
    close $fh;
    return $content;
}

my $pass = 0;
my $fail = 0;
my $total = 0;

sub assert {
    my ($condition, $test_name) = @_;
    $total++;
    if ($condition) {
        $pass++;
        print "  PASS: $test_name\n";
    } else {
        $fail++;
        print "  FAIL: $test_name\n";
    }
}

sub section {
    print "\n=== $_[0] ===\n";
}

# ============================================================================
# 1. Firestore Rules Validation
# ============================================================================

section("1. Firestore Security Rules - Anti-Inflation");

my $rules_path = "Assets/Scripts/CloudFunctions/firestore.rules";
my $rules = read_file($rules_path);
if (!$rules) {
    print "FATAL: Cannot read $rules_path\n";
    exit 1;
}

# 1a. Rules file must exist and be non-trivial
assert(length($rules) > 500, "firestore.rules is non-trivial (>500 chars)");

# 1b. Anti-inflation rule: score must be >= current score
assert($rules =~ /request\.resource\.data\.score\s*>=\s*resource\.data\.score/,
    "Anti-inflation rule: new score >= current score");

# 1c. Score must be int on create
assert($rules =~ /request\.resource\.data\.score\s+is\s+int/,
    "Score type check: score is int on create");

# 1d. Score must be > 0 on create
assert($rules =~ /request\.resource\.data\.score\s*>\s*0/,
    "Minimum score check: score > 0 on create");

# 1e. Read requires authentication
assert($rules =~ /allow read:.*request\.auth\s*!=\s*null/s,
    "Leaderboard read requires authentication");

# 1f. Create requires matching UID
assert($rules =~ /request\.auth\.uid\s*==\s*playerId/,
    "Create requires caller UID == playerId");

# 1g. Update requires matching UID
assert($rules =~ /allow update:.*request\.auth\.uid\s*==\s*playerId/s,
    "Update requires caller UID == playerId");

# 1h. Delete is blocked
# Match the pattern near the leaderboard section: allow delete: if false
my $lb_section = "";
if ($rules =~ m{(match /leaderboard.*?match /\{document)}s) {
    $lb_section = $1;
}
assert($lb_section =~ /allow delete:\s*if\s*false/,
    "Delete is blocked on leaderboard entries");

# 1i. Update field whitelist limits what can be changed
assert($rules =~ /hasOnly\(\s*\[\s*\n\s*'score'.*'playerName'.*'submittedAt'/s,
    "Update field whitelist includes score, playerName, submittedAt");

# 1j. Player name length is bounded (1-24 chars)
assert($rules =~ /playerName\.size\(\)\s*>\s*0.*playerName\.size\(\)\s*<=\s*24/s,
    "Player name length bounded to 1-24 chars");

# 1k. Global deny-all block exists at the bottom
assert($rules =~ /match\s+\/\{document=\*\*\}.*allow read,\s*write:\s*if\s*false/s,
    "Global deny-all catch-all block exists");

# 1l. Verify the leaderboard path structure is correct
assert($rules =~ /match\s+\/leaderboard\/\{seasonId\}\/entries\/\{playerId\}/,
    "Leaderboard path structure: leaderboard/{seasonId}/entries/{playerId}");

# ============================================================================
# 2. Client-Side Anti-Inflation Logic (parsed from C# source)
# ============================================================================

section("2. Client-Side Anti-Inflation Logic");

my $lbm_path = "Assets/Scripts/Leaderboard/LeaderboardManager.cs";
my $lbm = read_file($lbm_path);
if (!$lbm) {
    print "FATAL: Cannot read $lbm_path\n";
    exit 1;
}

# 2a. Score <= 0 is rejected
assert($lbm =~ /score\s*<=\s*0.*Score must be greater than zero/s,
    "Client rejects score <= 0");

# 2b. Empty player name is rejected
assert($lbm =~ /string\.IsNullOrWhiteSpace\(playerName\).*Player name cannot be empty/s,
    "Client rejects empty player name");

# 2c. Player name too long is rejected
assert($lbm =~ /playerName\.Length\s*>\s*maxPlayerNameLength/s,
    "Client rejects player name exceeding max length");

# 2d. Score <= local best is rejected (anti-inflation)
assert($lbm =~ /_localBest\s*!=\s*null\s*&&\s*score\s*<=\s*_localBest\.Score/s,
    "Client rejects score not higher than local best");

# 2e. Authentication check exists
assert($lbm =~ /string\.IsNullOrEmpty\(uid\).*Not authenticated/s,
    "Client checks authentication before submission");

# 2f. Optimistic update sets the local best
assert($lbm =~ /_localBest\s*=\s*new\s+LeaderboardEntry/s,
    "Optimistic update sets _localBest on submission");

# 2g. Rollback exists on failure
assert($lbm =~ /Roll back optimistic update/s,
    "Rollback comment exists for failed submissions");

# ============================================================================
# 3. Simulate Anti-Inflation State Machine
# ============================================================================

section("3. Simulate Anti-Inflation State Machine");

# Simulate a local LeaderboardEntry
my %local_best = (score => 0);

sub simulate_submit {
    my ($score, $player_name) = @_;
    my @errors;

    # Client validation: score > 0
    if ($score <= 0) {
        push @errors, "Score must be greater than zero.";
    }

    # Client validation: name not empty
    if (!$player_name || $player_name =~ /^\s*$/) {
        push @errors, "Player name cannot be empty.";
    }

    # Client validation: name length
    if ($player_name && length($player_name) > 24) {
        push @errors, "Player name cannot exceed 24 characters.";
    }

    # Client anti-inflation: score > local best
    if ($local_best{score} > 0 && $score <= $local_best{score}) {
        push @errors, "Score $score is not higher than your current best ($local_best{score}).";
    }

    if (@errors) {
        return { accepted => 0, errors => \@errors };
    }

    # Accept: update local best
    $local_best{score} = $score;
    return { accepted => 1, new_score => $score };
}

# 3a. First submission with valid score should be accepted
%local_best = (score => 0);
my $r = simulate_submit(100, "Player1");
assert($r->{accepted} == 1, "First submission (100) accepted");
assert($local_best{score} == 100, "Local best updated to 100");

# 3b. Lower score should be rejected
$r = simulate_submit(50, "Player1");
assert($r->{accepted} == 0, "Lower score (50) rejected");
assert(scalar @{$r->{errors}} > 0, "Rejection has error message");
assert($r->{errors}[0] =~ /not higher/, "Error mentions 'not higher'");

# 3c. Equal score should be rejected
$r = simulate_submit(100, "Player1");
assert($r->{accepted} == 0, "Equal score (100) rejected");

# 3d. Higher score should be accepted
$r = simulate_submit(200, "Player1");
assert($r->{accepted} == 1, "Higher score (200) accepted");
assert($local_best{score} == 200, "Local best updated to 200");

# 3e. Zero score should be rejected
$r = simulate_submit(0, "Player1");
assert($r->{accepted} == 0, "Zero score rejected");

# 3f. Negative score should be rejected
$r = simulate_submit(-10, "Player1");
assert($r->{accepted} == 0, "Negative score rejected");

# 3g. Empty name should be rejected
$r = simulate_submit(300, "");
assert($r->{accepted} == 0, "Empty name rejected");

# 3h. Whitespace-only name should be rejected
$r = simulate_submit(300, "   ");
assert($r->{accepted} == 0, "Whitespace-only name rejected");

# 3i. Name > 24 chars should be rejected
$r = simulate_submit(300, "A" x 25);
assert($r->{accepted} == 0, "Name > 24 chars rejected");

# 3j. Name exactly 24 chars should be accepted
$r = simulate_submit(300, "A" x 24);
assert($r->{accepted} == 1, "Name exactly 24 chars accepted");

# 3k. Reset local best, then zero score still rejected
%local_best = (score => 0);
$r = simulate_submit(0, "Player1");
assert($r->{accepted} == 0, "Zero score rejected even with no local best");

# ============================================================================
# 4. Simulate Cache Invalidation Logic
# ============================================================================

section("4. Simulate Cache Invalidation Logic");

my %cache;
my %cache_ts;

sub cache_key { return "top_$_[0]_$_[1]" }

sub simulate_invalidate {
    my ($season_id) = @_;
    my @removed;
    if (!defined $season_id) {
        @removed = keys %cache;
        %cache = ();
        %cache_ts = ();
    } else {
        for my $key (keys %cache) {
            if ($key =~ /\Q$season_id\E$/) {
                push @removed, $key;
                delete $cache{$key};
                delete $cache_ts{$key};
            }
        }
    }
    return @removed;
}

# 4a. Populate cache with multiple seasons
%cache = (
    "top_10_season_1" => [1, 2, 3],
    "top_20_season_1" => [4, 5],
    "top_10_season_2" => [6, 7],
);
%cache_ts = (
    "top_10_season_1" => 1.0,
    "top_20_season_1" => 1.0,
    "top_10_season_2" => 1.0,
);

# 4b. Invalidate specific season
my @removed = simulate_invalidate("season_1");
assert(scalar @removed == 2, "Invalidating season_1 removes 2 entries");
assert(!exists $cache{"top_10_season_1"}, "top_10_season_1 removed");
assert(!exists $cache{"top_20_season_1"}, "top_20_season_1 removed");
assert(exists $cache{"top_10_season_2"}, "top_10_season_2 preserved");

# 4c. Invalidate all seasons
@removed = simulate_invalidate(undef);
assert(scalar @removed == 1, "Invalidating all removes remaining 1 entry");
assert(scalar keys %cache == 0, "Cache is empty after full invalidation");

# 4d. Cache key format is deterministic
assert(cache_key(10, "season_1") eq "top_10_season_1",
    "Cache key format: top_{count}_{seasonId}");

# ============================================================================
# Summary
# ============================================================================

print "\n" . ("=" x 60) . "\n";
print "RESULTS: $pass/$total passed, $fail failed\n";
print "=" x 60, "\n";

exit($fail > 0 ? 1 : 0);
