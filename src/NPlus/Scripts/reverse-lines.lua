-- reverse-lines.lua
-- Reverses the order of every line in the active document.
--
-- Demonstrates: editor.lines() / editor.set_lines() for whole-buffer transforms.

local lines = editor.lines()

local reversed = {}
local n = #lines
for i = 1, n do
  reversed[i] = lines[n - i + 1]
end

editor.set_lines(reversed)
print(string.format("Reversed %d line(s).", n))
