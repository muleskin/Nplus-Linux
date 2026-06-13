-- uppercase-selection.lua
-- Upper-cases the selected text. With no selection, upper-cases the whole document.
--
-- Demonstrates: editor.selection() / editor.replace_selection() and a whole-buffer
-- fallback via editor.text() / editor.set_text().

local sel = editor.selection()

if sel ~= "" then
  editor.replace_selection(sel:upper())
  print(string.format("Upper-cased %d selected character(s).", #sel))
else
  local all = editor.text()
  editor.set_text(all:upper())
  print("No selection — upper-cased the entire document.")
end
