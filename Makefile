# Makefile for n+ (Linux/Avalonia edition)
# Requires the .NET 10 SDK on PATH. Override variables on the command line, e.g.:
#   make publish RID=linux-arm64
#   make build CONFIG=Debug

# Pick a dotnet whose SDK can target net10 (major version >= 10), checking the per-user
# dotnet-install.sh location (~/.dotnet) then PATH — this skips an older system SDK
# (e.g. /usr/bin/dotnet 6.x). Override with: make <target> DOTNET=/path/to/dotnet
DOTNET  ?= $(shell best=dotnet; for c in "$(HOME)/.dotnet/dotnet" "$$(command -v dotnet 2>/dev/null)"; do [ -x "$$c" ] || continue; "$$c" --list-sdks 2>/dev/null | grep -qE '^[1-9][0-9]+\.' && { best="$$c"; break; }; done; echo "$$best")
PROJECT := src/NPlus/NPlus.csproj
CONFIG  ?= Release

# Auto-detect the host architecture so 'make publish' produces a native binary.
# Override on the command line, e.g.  make publish RID=linux-x64
UNAME_M := $(shell uname -m)
ifeq ($(UNAME_M),x86_64)
  RID ?= linux-x64
else ifeq ($(UNAME_M),aarch64)
  RID ?= linux-arm64
else ifeq ($(UNAME_M),arm64)
  RID ?= linux-arm64
else ifeq ($(UNAME_M),armv7l)
  RID ?= linux-arm
else
  RID ?= linux-x64
endif

.DEFAULT_GOAL := build
.PHONY: all help restore build run release publish package install uninstall clean

all: build

help:
	@echo "n+ build targets:"
	@echo "  make build       - build ($(CONFIG)) the editor"
	@echo "  make run         - build & run the editor (dev)"
	@echo "  make release     - build in Release configuration"
	@echo "  make publish     - self-contained single-file binary -> dist/$(RID)"
	@echo "  make package     - publish + assemble dist/nplus-$(RID).tar.gz"
	@echo "  make install     - package then install per-user into ~/.local"
	@echo "  make uninstall    - remove the per-user install"
	@echo "  make clean        - remove dist/, bin/, obj/"
	@echo ""
	@echo "Variables: CONFIG=$(CONFIG)  RID=$(RID)  (e.g. 'make publish RID=linux-arm64')"

restore:
	$(DOTNET) restore $(PROJECT)

build:
	$(DOTNET) build $(PROJECT) -c $(CONFIG)

run:
	$(DOTNET) run --project $(PROJECT)

release:
	$(DOTNET) build $(PROJECT) -c Release

publish:
	$(DOTNET) publish $(PROJECT) -c Release -r $(RID) --self-contained true \
		-p:PublishSingleFile=true -o dist/$(RID)
	@echo "Built dist/$(RID)/nplus"

package:
	DOTNET="$(DOTNET)" bash packaging/build-linux.sh $(RID)

install: package
	cd dist/nplus-$(RID) && ./install.sh

uninstall:
	rm -rf "$(HOME)/.local/lib/nplus" \
		"$(HOME)/.local/bin/nplus" \
		"$(HOME)/.local/share/applications/nplus.desktop" \
		"$(HOME)/.local/share/icons/hicolor/256x256/apps/nplus.png"
	@echo "Removed per-user install."

clean:
	rm -rf dist
	-$(DOTNET) clean $(PROJECT) -c $(CONFIG)
	find src -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
