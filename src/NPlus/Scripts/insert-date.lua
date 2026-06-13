-- insert-date.lua
-- Inserts the current date and time at the caret, e.g. 2026-06-12 14:05:00.
--
-- Demonstrates: editor.insert() plus Lua's os.date (available in the sandbox).

local stamp = os.date("%Y-%m-%d %H:%M:%S")
editor.insert(stamp)
print("Inserted timestamp: " .. stamp)
