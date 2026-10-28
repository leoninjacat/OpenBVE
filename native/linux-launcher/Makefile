CC ?= cc
PKGS = gtk4 libadwaita-1 libxml-2.0
CPPFLAGS += $(shell pkg-config --cflags $(PKGS))
CFLAGS ?= -O2 -g
CFLAGS += -Wall -Wextra -Wpedantic
LDLIBS += $(shell pkg-config --libs $(PKGS))

all: build/openbve-launcher
build/openbve-launcher: main.c preview.c preview.h content-path.h
	mkdir -p build
	$(CC) $(CPPFLAGS) $(CFLAGS) main.c preview.c -o $@ $(LDLIBS)
check: build/test-preview
	./build/test-preview
build/test-preview: test-preview.c preview.c preview.h
	mkdir -p build
	$(CC) $(CPPFLAGS) $(CFLAGS) test-preview.c preview.c -o $@ $(LDLIBS)
.PHONY: all check

check-ui: build/test-start
	./build/test-start
build/test-start: test-start.c main.c preview.c preview.h content-path.h
	mkdir -p build
	$(CC) $(CPPFLAGS) $(CFLAGS) test-start.c preview.c -o $@ $(LDLIBS)
.PHONY: check-ui

check-paths: build/test-paths
	./build/test-paths
build/test-paths: test-paths.c content-path.h
	mkdir -p build
	$(CC) $(CPPFLAGS) $(CFLAGS) test-paths.c -o $@ $(LDLIBS)
.PHONY: check-paths

build/route-picker: route-picker.c content-path.h
	mkdir -p build
	$(CC) $(CPPFLAGS) $(CFLAGS) route-picker.c -o $@ $(LDLIBS)

build/route-editor: route-editor.c
	mkdir -p build
	$(CC) $(shell pkg-config --cflags gtk4 libadwaita-1 x11) $(CFLAGS) $< -o $@ $(shell pkg-config --libs gtk4 libadwaita-1 x11)

build/test-route-editor: test-route-editor.c route-editor.c
	mkdir -p build
	$(CC) $(shell pkg-config --cflags gtk4 libadwaita-1 x11) $(CFLAGS) $< -o $@ $(shell pkg-config --libs gtk4 libadwaita-1 x11)
check-editor: build/test-route-editor
	./build/test-route-editor
.PHONY: check-editor

build/test-object-creation: test-object-creation.c route-editor.c
	mkdir -p build
	$(CC) $(shell pkg-config --cflags gtk4 libadwaita-1 x11) $(CFLAGS) $< -o $@ $(shell pkg-config --libs gtk4 libadwaita-1 x11)
check-object-creation: build/test-object-creation
	./build/test-object-creation
.PHONY: check-object-creation
