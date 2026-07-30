const assert = require("node:assert/strict");
const test = require("node:test");
const { detectClipboardExtension } = require("./clipboard.cjs");
const { safeMediaName } = require("./youtube.cjs");

test("detects the clipboard formats supported by the renderer", () => {
  assert.equal(detectClipboardExtension('{"ready":true}'), "json");
  assert.equal(detectClipboardExtension("import pathlib\nprint(pathlib.Path.cwd())"), "py");
  assert.equal(detectClipboardExtension("interface User { name: string }"), "ts");
  assert.equal(detectClipboardExtension("# Clipboard notes\n\n- one\n- two"), "md");
  assert.equal(detectClipboardExtension("name,port\nwindows,3000\nwsl,3001"), "csv");
  assert.equal(detectClipboardExtension("@echo off\nsetlocal\nset NAME=value"), "bat");
});

test("normalizes Windows-unsafe media names", () => {
  assert.equal(safeMediaName('  My: "Workout" / Part 1  ', "Video"), "My_Workout_Part_1");
  assert.equal(safeMediaName("<>:\"/\\|?*", "Video"), "Video");
});
