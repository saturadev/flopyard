#if UNITY_EDITOR && SATURA_SPACE
using SaturaSpace;
#endif
using UnityEngine;

namespace Flopyard
{
    public static class FlopyardHandles
    {
#if UNITY_EDITOR && SATURA_SPACE
        [SaturaHandle("Restarts the run: fresh hero, score 0, first sparring sack walks in")]
        public static string StartRun()
        {
            var g = FlopyardGame.Instance;
            if (!g) return "no game";
            g.StartRun();
            return "started";
        }

        [SaturaHandle("Turns the demo bot on or off: it walks the hero up to the nearest opponent and punches and kicks")]
        public static string DemoRun(bool on = true)
        {
            var g = FlopyardGame.Instance;
            if (!g) return "no game";
            g.autoPlay = on;
            return "autoPlay=" + on;
        }

        [SaturaHandle("The hero punches once")]
        public static bool Punch() => FlopyardGame.Instance && FlopyardGame.Instance.hero.Punch();

        [SaturaHandle("The hero kicks once")]
        public static bool Kick() => FlopyardGame.Instance && FlopyardGame.Instance.hero.Kick();

        [SaturaHandle("Knocks the hero down (to check the grey down screen and getting up)")]
        public static string KnockHeroDown()
        {
            var g = FlopyardGame.Instance;
            g.hero.KnockDown(-g.hero.Pelvis.transform.forward);
            return "down";
        }

        [SaturaHandle("Game state: hero hp, down, KOs, opponents alive, pelvis heights")]
        public static string State()
        {
            var g = FlopyardGame.Instance;
            if (!g || !g.hero) return "no game";
            var h = g.hero;
            var s = $"t={g.runTime:F1} hp={h.hp:F0} down={h.down} ko={h.knockedOut} kos={g.kos} downs={g.heroDowns} heroPos={h.Position} torsoUp={Vector3.Angle(h.Torso.transform.up, Vector3.up):F0}deg muscle={h.Muscle:F2}";
            foreach (var e in g.enemies)
                if (e) s += $"\n  {e.kind} hp={e.hp:F0} down={e.down} ko={e.knockedOut} pos={e.Position} up={Vector3.Angle(e.Torso.transform.up, Vector3.up):F0}deg";
            return s;
        }
#endif
    }
}
