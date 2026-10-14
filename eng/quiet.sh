#!/usr/bin/env bash
# Runs gate steps quietly. The terminal gets one line per step (for a Fallout run, its target
# table), then PASS or FAIL with the total time and the log path; a failing step adds its last 60
# lines (for a Fallout run, the failing target's). A Fallout run that plans dotcov's
# ReportCoverage also prints the coverage verdict, and its exit code is named. The whole output
# goes to artifacts/logs/<name>.log. The exit code is the failing step's, else 0. QYL_VERBOSE=1
# streams everything to the terminal instead.
#
#   eng/quiet.sh <name> '<command>' ['<command>' ...]  each command through bash, in order,
#                                                         stopping at the first failure
#   eng/quiet.sh <name> -- <program> [<argument> ...]  one step with exactly this argv
#
# Everything runs inside main, which bash parses whole before running, so editing this file
# while a gate runs cannot change the running gate.
set -uo pipefail

duration() {
  if (($1 >= 60)); then printf '%dm%02ds' $(($1 / 60)) $(($1 % 60)); else printf '%ds' "$1"; fi
}

# Drops colour and cursor sequences, keeps the last state of a carriage-return progress line,
# turns Fallout's no-break spaces into spaces and drops its zero-width ones.
plain() {
  perl -pe 'BEGIN { $| = 1 } s/\e\[[0-?]*[ -\/]*[@-~]//g; s/\e\][^\a\e]*(?:\a|\e\\)//g; s/^.*\r(?=.)//; s/\r$//; s/\xc2\xa0/ /g; s/\xe2\x80\x8b//g'
}

# The rows of the last Fallout target table in a step's output. Targets that succeeded in under a
# second without a note, and targets a failure kept from running, are counted instead of listed.
fallout_rows() {
  perl -ne 'if (/^Target\s+Status\s+Duration\s*$/) { @rows = (); $in = 1; next }
    if ($in && /^Total\s/) { $in = 0; next }
    if ($in && /^\S+\s+(?:Succeeded|Failed|Skipped|NotRun|Aborted)\b/) { s/\s+$//; push @rows, $_ }
    END {
      my ($quick, $notrun) = (0, 0);
      for (@rows) {
        if (/^\S+\s+Succeeded\s+< 1sec$/) { $quick++ } elsif (/^\S+\s+NotRun$/) { $notrun++ } else { print "$_\n" }
      }
      print "$quick more target", ($quick == 1 ? "" : "s"), " succeeded in under a second\n" if $quick;
      print "$notrun target", ($notrun == 1 ? "" : "s"), " not run\n" if $notrun;
    }' "$1"
}

# dotcov's coverage gate in a step's output, when its Fallout run planned ReportCoverage: the
# verdict line (PASS, FAIL, NODATA or DISABLED with both rates and their minimums), or a failed
# test run, which the Coverage targets report like `dotcov test`. DotCov.Fallout logs the verdict
# in the build's own output; `dotcov` the CLI writes it as the first stderr line, which reaches
# the step output too.
coverage_verdict() {
  perl -ne '$gate = 1 if /^ReportCoverage\s+(?:Succeeded|Failed|Skipped|NotRun|Aborted)\b/;
    $verdict = $1 if /\b((?:PASS|FAIL|NODATA|DISABLED): line \S+ \(min [^)]*\), branch .*?)\s*$/
      || /\b(dotnet test exited with code -?\d+; coverage was not evaluated\.)/;
    END { print "coverage gate: $verdict\n" if $gate && defined $verdict }' "$1"
}

# dotcov's exit code named, for a step whose Fallout run planned ReportCoverage. Any other failed
# target exits 255 and stays a number.
coverage_exit() {
  perl -ne 'if (/^ReportCoverage\s+(?:Succeeded|Failed|Skipped|NotRun|Aborted)\b/) { $gate = 1; last }
    END { exit !$gate }' "$1" || return 0
  case $2 in
    1) printf ', coverage FAIL' ;;
    2) printf ', invalid coverage parameter' ;;
    3) printf ', coverage NODATA' ;;
    4) printf ', coverage DISABLED' ;;
    5) printf ', coverage report unreadable' ;;
    6) printf ', test run failed' ;;
  esac
}

# The last lines of each failed Fallout target's block (at most three), or of the whole output.
failure_tail() {
  perl -e 'my ($file, $budget) = @ARGV; open my $in, "<", $file or die; my @l = <$in>; chomp @l;
    my (@rows, $table);
    for (@l) {
      if (/^Target\s+Status\s+Duration\s*$/) { @rows = (); $table = 1; next }
      if ($table && /^Total\s/) { $table = 0; next }
      push @rows, $1 if $table && /^(\S+)\s+(?:Failed|Aborted)\b/;
    }
    my @blocks;
    for my $target (@rows[0 .. ($#rows < 2 ? $#rows : 2)]) {
      my ($header) = grep { $l[$_] =~ /^\x{e2}\x{95}\x{91} \Q$target\E\s*$/ } reverse 0 .. $#l;
      next unless defined $header;
      my $first = $header + 2; my $last = $first;
      $last++ while $last <= $#l && $l[$last] !~ /^\x{e2}\x{95}\x{ac}/;
      $first++ while $first < $last && $l[$first] =~ /^\s*$/;
      $last-- while $last > $first && $l[$last - 1] =~ /^\s*$/;
      push @blocks, [$target, $first, $last - 1];
    }
    if (!@blocks) { my $from = @l > $budget ? @l - $budget : 0; print "$_\n" for @l[$from .. $#l]; exit }
    my $each = int($budget / @blocks); $each = 20 if $each < 20;
    for (@blocks) {
      my ($target, $first, $last) = @$_;
      my $from = $last - $first + 1 > $each ? $last - $each + 1 : $first;
      print "--- $target, last ", $last - $from + 1, " lines\n";
      print "$_\n" for @l[$from .. $last];
    }' "$1" 60
}

main() {
  local name=$1 root single=0 command
  shift
  root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
  if [[ ${1:-} == -- ]]; then
    single=1
    shift
  fi

  if [[ ${QYL_VERBOSE:-0} == 1 ]]; then
    if ((single)); then
      exec "$@"
    fi
    for command in "$@"; do
      printf '$ %s\n' "$command" >&2
      bash -ec "$command" || return
    done
    return 0
  fi

  log=$root/artifacts/logs/$name.log
  work=$(mktemp -d "${TMPDIR:-/tmp}/quiet.XXXXXX")
  # The log is written outside the repository and moved into place on exit, so a target that
  # cleans artifacts/ cannot delete it mid-run.
  trap 'mkdir -p "${log%/*}" && mv -f "$work/log" "$log"; rm -rf "$work"' EXIT
  : >"$work/log"

  local started=$SECONDS status=0 failed= named=
  run_step() {
    local label=$1 start=$SECONDS indent= word=PASS
    shift
    printf '\n===== %s\n' "$label" >>"$work/log"
    QYL_VERBOSE=1 "$@" 2>&1 | plain | tee -a "$work/log" >"$work/step"
    status=${PIPESTATUS[0]}
    ((status == 0)) || word=FAIL
    if ((!single)); then
      printf '%s %6s  %s\n' "$word" "$(duration $((SECONDS - start)))" "$label"
      indent='     '
      ((status == 0)) || failed=": $label"
    fi
    fallout_rows "$work/step" | sed "s/^/$indent/"
    coverage_verdict "$work/step" | sed "s/^/$indent/"
    ((status == 0)) || named=$(coverage_exit "$work/step" "$status")
    ((status == 0)) || failure_tail "$work/step"
  }

  if ((single)); then
    run_step "$*" "$@"
  else
    for command in "$@"; do
      run_step "$command" bash -ec "$command"
      ((status == 0)) || break
    done
  fi

  if ((status == 0)); then
    printf 'PASS %s in %s, log %s\n' "$name" "$(duration $((SECONDS - started)))" "$log"
  else
    printf 'FAIL %s in %s (exit %d%s%s), log %s\n' "$name" "$(duration $((SECONDS - started)))" "$status" "$named" "$failed" "$log"
  fi
  return "$status"
}

main "$@"; exit
