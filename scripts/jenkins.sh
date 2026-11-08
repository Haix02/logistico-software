#!/usr/bin/env bash
# Runs the Logistico Jenkins on this computer (docs/11 §7): installs its plugins, generates its administrator password,
# and starts it from deploy/jenkins/jenkins.yaml.
#
#   scripts/jenkins.sh setup     once: download the plugins, create the password
#   scripts/jenkins.sh start     start in the background on 127.0.0.1:8090
#   scripts/jenkins.sh stop | status | logs | password
#
# Settings (environment): JENKINS_HOME (~/.logistico-jenkins), JENKINS_PORT (8090), JENKINS_WAR, JAVA_HOME,
# LOGISTICO_TEST_DB (a connection string whose role may create databases).
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
home="${JENKINS_HOME:-$HOME/.logistico-jenkins}"
port="${JENKINS_PORT:-8090}"
plugin_manager_version="2.16.0"
pid_file="$home/jenkins.pid"
secrets="$home/logistico-secrets"

find_war() {
  for war in "${JENKINS_WAR:-}" /opt/homebrew/opt/jenkins-lts/libexec/jenkins.war /usr/local/opt/jenkins-lts/libexec/jenkins.war \
             /usr/share/java/jenkins.war /usr/share/jenkins/jenkins.war; do
    [ -n "$war" ] && [ -f "$war" ] && { echo "$war"; return; }
  done
  echo "Jenkins not found. Install it (macOS: brew install jenkins-lts) or set JENKINS_WAR." >&2
  exit 1
}

find_java() {
  for java in "${JAVA_HOME:+$JAVA_HOME/bin/java}" /opt/homebrew/opt/openjdk@21/bin/java /usr/local/opt/openjdk@21/bin/java \
              /usr/lib/jvm/java-21-openjdk-amd64/bin/java; do
    [ -n "$java" ] && [ -x "$java" ] && { echo "$java"; return; }
  done
  echo "Java 21 not found. Install it (macOS: brew install openjdk@21) or set JAVA_HOME." >&2
  exit 1
}

running() { [ -f "$pid_file" ] && kill -0 "$(cat "$pid_file")" 2>/dev/null; }

setup() {
  mkdir -p "$home/plugins" "$secrets"
  chmod 700 "$home" "$secrets"
  local java war manager
  java="$(find_java)"; war="$(find_war)"
  manager="$home/jenkins-plugin-manager-$plugin_manager_version.jar"
  if [ ! -f "$manager" ]; then
    curl -fsSL -o "$manager" \
      "https://github.com/jenkinsci/plugin-installation-manager-tool/releases/download/$plugin_manager_version/jenkins-plugin-manager-$plugin_manager_version.jar"
  fi
  "$java" -jar "$manager" --war "$war" --plugin-download-directory "$home/plugins" --plugin-file "$repo/deploy/jenkins/plugins.txt"
  if [ ! -f "$secrets/admin-password" ]; then
    (umask 077; openssl rand -base64 18 | tr -d '/+=' > "$secrets/admin-password")
    echo "Administrator password written to $secrets/admin-password"
  fi
}

start() {
  running && { echo "Already running: http://127.0.0.1:$port/"; return; }
  [ -f "$secrets/admin-password" ] || setup
  local java war test_db
  java="$(find_java)"; war="$(find_war)"
  test_db="${LOGISTICO_TEST_DB:-Host=localhost;Port=5432;Database=logistico_test;Username=logistico;Password=logistico_dev}"

  # The build tools Jenkins needs, from the shell that starts it; login shells on macOS keep Homebrew in PATH.
  JENKINS_HOME="$home" \
  JENKINS_ADMIN_PASSWORD="$(cat "$secrets/admin-password")" \
  JENKINS_URL="http://127.0.0.1:$port/" \
  LOGISTICO_TEST_DB="$test_db" \
  CASC_JENKINS_CONFIG="$repo/deploy/jenkins/jenkins.yaml" \
  PATH="/opt/homebrew/bin:/usr/local/bin:/usr/local/share/dotnet:$HOME/.dotnet/tools:$PATH" \
  nohup "$java" -Djava.awt.headless=true -Djenkins.install.runSetupWizard=false \
    -jar "$war" --httpListenAddress=127.0.0.1 --httpPort="$port" > "$home/jenkins.log" 2>&1 &
  echo $! > "$pid_file"

  for _ in $(seq 1 120); do
    code="$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port/login" || true)"
    [ "$code" = "200" ] && { echo "Jenkins is up: http://127.0.0.1:$port/ (user admin, password: scripts/jenkins.sh password)"; return; }
    running || { echo "Jenkins stopped while starting; see $home/jenkins.log" >&2; exit 1; }
    # A configuration Jenkins refuses (deploy/jenkins/jenkins.yaml) leaves it half up: say why and stop it.
    if grep -q "BootFailure" "$home/jenkins.log" 2>/dev/null; then
      echo "Jenkins could not start:" >&2
      grep -m1 -A1 "SEVERE" "$home/jenkins.log" >&2
      stop >/dev/null
      exit 1
    fi
    sleep 2
  done
  echo "Jenkins did not answer in time; see $home/jenkins.log" >&2
  exit 1
}

stop() {
  if running; then
    kill "$(cat "$pid_file")"
    rm -f "$pid_file"
    echo "Stopped."
  else
    echo "Not running."
  fi
}

case "${1:-}" in
  setup) setup ;;
  start) start ;;
  stop) stop ;;
  status) running && echo "Running: http://127.0.0.1:$port/ (pid $(cat "$pid_file"))" || echo "Not running." ;;
  logs) tail -n 100 -f "$home/jenkins.log" ;;
  password) cat "$secrets/admin-password" ;;
  *) sed -n '2,10p' "${BASH_SOURCE[0]}"; exit 2 ;;
esac
