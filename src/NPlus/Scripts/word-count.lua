-- word-count.lua
-- Reports line, word and character counts for the active document.
-- Read-only: it never modifies the buffer, only prints to the script output.
--
-- Demonstrates: editor.text() / editor.lines() and printing a report.

local text = editor.text()

local chars = #text
local lines = #editor.lines()

local words = 0
for _ in text:gmatch("%S+") do
  words = words + 1
end

print("Document statistics")
print("-------------------")
print(string.format("Lines:      %d", lines))
print(string.format("Words:      %d", words))
print(string.format("Characters: %d", chars))

local path = editor.file_path()
if path then print("File:       " .. path) end
