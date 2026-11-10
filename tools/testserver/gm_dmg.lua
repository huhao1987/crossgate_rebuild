-- 测试用：背包里有耐久的道具耐久减半（/dolua gm_dmg.lua）
return function(charIndex, args)
  local out = {}
  for slot = 8, 27 do
    local it = Char.GetItemIndex(charIndex, slot)
    if it and it >= 0 then
      local mx = Item.GetData(it, CONST.道具_最大耐久) or 0
      if mx > 0 then
        Item.SetData(it, CONST.道具_耐久, math.floor(mx / 2))
        Item.UpItem(charIndex, slot)
        table.insert(out, slot .. ':' .. Item.GetData(it, CONST.道具_耐久) .. '/' .. mx .. ':' .. tostring(Item.GetData(it, CONST.道具_已鉴定)))
      end
    end
  end
  NLG.SystemMessage(charIndex, 'gm_dmg ' .. table.concat(out, ' '))
end
