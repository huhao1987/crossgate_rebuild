cd /home/claude/cgport
G=/home/claude/Godot_v4.3-stable_mono_linux_x86_64/Godot_v4.3-stable_mono_linux.x86_64
: > /tmp/runall.out
for f in test/Net*.cs test/BattleStatus.cs; do t=$(basename $f .cs)
  case $t in NetJobX|NetGuild|NetCard|NetDuel|NetParty|NetTrade|NetTradePet|NetPetMail|NetShot) continue;; esac
  bash /home/claude/wine/reset_test1.sh >/dev/null 2>&1
  timeout 15 bash -c 'while :; do :; done'
  pkill -f "Xvfb :97"; rm -f /tmp/.X97-lock
  r=$(timeout 300 xvfb-run -n 97 -s "-screen 0 1280x800x24" $G --rendering-driver opengl3 --resolution 960x720 --path . -s res://$f 2>&1 | grep -aE "^(PASS|FAIL)" | sort -u)
  echo "== $t $(echo "$r" | grep -c ^PASS) pass $(echo "$r" | grep -c ^FAIL) fail" >> /tmp/runall.out
  echo "$r" | grep ^FAIL | head -5 >> /tmp/runall.out
  timeout 10 bash -c 'while :; do :; done'
done
echo ALLDONE >> /tmp/runall.out
