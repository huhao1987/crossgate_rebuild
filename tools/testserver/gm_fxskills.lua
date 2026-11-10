-- test: give skills for effect capture (/dolua gm_fxskills.lua)
return function(charIndex, args)
  for _, s in ipairs({225, 216, 219, 217, 223, 227, 220}) do
    if Char.HaveSkill(charIndex, s) >= 0 then Char.DelSkill(charIndex, s) end
  end
  for _, s in ipairs({1, 3, 5, 31, 32, 33, 34, 55, 67, 68}) do
    if Char.HaveSkill(charIndex, s) < 0 then Char.AddSkill(charIndex, s) end
    local slot = Char.HaveSkill(charIndex, s)
    if slot >= 0 then Char.SetSkillLevel(charIndex, slot, 1) end
  end
  Char.SetData(charIndex, 13, Char.GetData(charIndex, 458))
  NLG.UpChar(charIndex)
  NLG.SystemMessage(charIndex, 'gm_fxskills ok')
end
