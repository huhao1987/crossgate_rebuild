-- 测试用：背包 12–27 格全清（回归测试之间把背包恢复到干净状态；8–11 留着：金币 + 3 组药）
return function(charIndex, args)
  local n = 0
  for slot = 12, 27 do
    local ok, r = pcall(Char.DelItemBySlot, charIndex, slot)
    if ok and r == 0 then n = n + 1 end
  end
  NLG.SystemMessage(charIndex, 'gm_bagtrim ' .. n)
end
