# A thin wrapper over the Fallout build in eng/build: `make <target>` runs `./eng/build.sh <Target>`,
# and ARGS passes parameters through, e.g. `make demos ARGS="--lane quartz"`. Every target with its
# description and parameters: `make help`.

.PHONY: help build pack contracts api-baseline generator-snapshots package-layout consumer-behavior \
        aot-publish-gate demos live-check smoke webapi-aot-demo otlp-evidence check check-full testbed-probe

help:
	@./eng/build.sh --help

build:               ; ./eng/build.sh Build $(ARGS)
pack:                ; ./eng/build.sh Pack $(ARGS)
contracts:           ; ./eng/build.sh Contracts $(ARGS)
api-baseline:        ; ./eng/build.sh ApiBaseline $(ARGS)
generator-snapshots: ; ./eng/build.sh GeneratorSnapshots $(ARGS)
package-layout:      ; ./eng/build.sh PackageLayout $(ARGS)
consumer-behavior:   ; ./eng/build.sh ConsumerBehavior $(ARGS)
aot-publish-gate:    ; ./eng/build.sh AotPublishGate $(ARGS)
demos:               ; ./eng/build.sh Demos $(ARGS)
live-check:          ; ./eng/build.sh LiveCheck $(ARGS)
smoke:               ; ./eng/build.sh Smoke $(ARGS)
webapi-aot-demo:     ; ./eng/build.sh WebApiAotDemo $(ARGS)
otlp-evidence:       ; ./eng/build.sh OtlpEvidence $(ARGS)
check:               ; ./eng/build.sh Check $(ARGS)
check-full:          ; ./eng/build.sh CheckFull $(ARGS)
testbed-probe:       ; ./eng/build.sh TestbedProbe $(ARGS)
