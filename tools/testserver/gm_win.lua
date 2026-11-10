-- 测试用：给自己弹任意窗口（/dolua gm_win.lua 类型 按钮掩码 数据）——数据里不能有空格
return function(charIndex, args)
  local t = tonumber(args[1]) or 0
  local b = tonumber(args[2]) or 0
  local d = args[3] or ''
  NLG.ShowWindowTalked(charIndex, charIndex, t, b, 1, d)
  NLG.SystemMessage(charIndex, 'gm_win ' .. t .. ' ' .. b)
end
