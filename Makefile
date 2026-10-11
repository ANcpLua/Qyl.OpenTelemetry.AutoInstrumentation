# Every verification lane of this repository, runnable one at a time or together. These are the jobs the
# former CI ran; `make verify` is the no-Docker set, `make all` adds the Docker demos and the live check.
# The tools come from mise.toml: run `mise install` once, then `make deps`.

SHELL := /bin/bash
SLN := Qyl.Telemetry.AutoInstrumentation.slnx

.PHONY: all verify deps build floor smoke otlp-receiver webapi-aot-demo aot-publish-gate demos live-check check help

help: ## List the targets
	@grep -E '^[a-z-]+:.*## ' $(MAKEFILE_LIST) | sed 's/:.*## /\t/'

all: verify demos live-check ## Everything: the no-Docker lanes, the real-library Docker demos and the live check

verify: build floor smoke otlp-receiver webapi-aot-demo aot-publish-gate ## The no-Docker lanes in order

deps: ## Python verifier dependencies
	python3 -m pip install -r tools/requirements.txt

build: ## Build the solution (Release)
	dotnet build $(SLN) --configuration Release

floor: ## The goal verifier without demos (what the former verify job ran, both shards)
	python3 tools/verify-aot-autoinstrumentation-goal.py --no-demos --skip "smoketest,webapi aot demo,otlp receiver evidence,live check,nativeaot publish gate"

smoke: ## Package smoke test
	bash tools/smoketest.sh

otlp-receiver: ## Typed OTLP receiver evidence
	python3 tools/verify-otlp-receiver.py

webapi-aot-demo: ## NativeAOT web API demo
	python3 tools/verify-webapi-aot-demo.py

aot-publish-gate: ## NativeAOT publish gate, every set, strict promotion (all four former shards)
	python3 tools/verify-aot-publish-gate.py --set all --strict-promotion

demos: ## Real-library demos (Docker or OrbStack must be running; the former six shards)
	python3 tools/verify-aot-autoinstrumentation-goal.py --demos-only

live-check: ## Live check against the pinned SemanticConventions registry (network)
	python3 tools/verify-live-check.py

check: ## The former CI gate (release build plus the goal verifier)
	mise run check
