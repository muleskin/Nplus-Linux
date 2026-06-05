# Makefile for n+ (Linux/Avalonia edition)
# Requires the .NET 10 SDK on PATH. Override variables on the command line, e.g.:
#   make publish RID=linux-arm64
#   make build CONFIG=Debug

DOTNET  ?= dotnet
PROJECT := src/NPlus/NPlus.csproj
CONFIG  ?= Release
RID     ?= linux-x64

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
	bash packaging/build-linux.sh $(RID)

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
