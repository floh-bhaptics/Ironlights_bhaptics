using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using MelonLoader;
using HarmonyLib;
using UnityEngine;

using MyBhapticsTactsuit;

namespace Ironlights_bhaptics
{
    public class Ironlights_bhaptics : MelonMod
    {
        public static TactsuitVR tactsuitVr;
        public static Stopwatch hitTimer = new Stopwatch();

        public override void OnApplicationStart()
        {
            base.OnApplicationStart();
            tactsuitVr = new TactsuitVR();
            tactsuitVr.PlaybackHaptics("HeartBeat");
            hitTimer.Start();
        }

        [HarmonyPatch(typeof(Fighter), "Retreat", new Type[] { })]
        public class bhaptics_Retreat
        {
            [HarmonyPostfix]
            public static void Postfix(Fighter __instance)
            {
                if (!__instance.isHost) return;
                tactsuitVr.PlaybackHaptics("Retreat");
            }
        }

        [HarmonyPatch(typeof(Fighter), "Die", new Type[] { })]
        public class bhaptics_Die
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                tactsuitVr.StopThreads();
            }
        }

        [HarmonyPatch(typeof(Fighter), "Blitz", new Type[] { })]
        public class bhaptics_Blitz
        {
            [HarmonyPostfix]
            public static void Postfix(Fighter __instance)
            {
                if (!__instance.isHost) return;
                tactsuitVr.PlaybackHaptics("Blitz");
            }
        }

        private static KeyValuePair<float, float> getAngleAndShift(Transform player, Vector3 hit)
        {
            // bhaptics pattern starts in the front, then rotates to the left. 0° is front, 90° is left, 270° is right.
            // y is "up", z is "forward" in local coordinates
            Vector3 patternOrigin = new Vector3(0f, 0f, 1f);
            Vector3 hitPosition = hit - player.position;
            Quaternion myPlayerRotation = player.rotation;
            Vector3 playerDir = myPlayerRotation.eulerAngles;
            // get rid of the up/down component to analyze xz-rotation
            Vector3 flattenedHit = new Vector3(hitPosition.x, 0f, hitPosition.z);

            // get angle. .Net < 4.0 does not have a "SignedAngle" function...
            float hitAngle = Vector3.Angle(flattenedHit, patternOrigin);
            // check if cross product points up or down, to make signed angle myself
            Vector3 crossProduct = Vector3.Cross(flattenedHit, patternOrigin);
            if (crossProduct.y > 0f) { hitAngle *= -1f; }
            // relative to player direction
            float myRotation = hitAngle - playerDir.y;
            // switch directions (bhaptics angles are in mathematically negative direction)
            myRotation *= -1f;
            // convert signed angle into [0, 360] rotation
            if (myRotation < 0f) { myRotation = 360f + myRotation; }


            // up/down shift is in y-direction
            // in Shadow Legend, the torso Transform has y=0 at the neck,
            // and the torso ends at roughly -0.5 (that's in meters)
            // so cap the shift to [-0.5, 0]...
            float hitShift = hitPosition.y;
            //tactsuitVr.LOG("HitShift: " + hitShift.ToString());
            float upperBound = 1.6f;
            float lowerBound = 0.5f;
            if (hitShift > upperBound) { hitShift = 0.5f; }
            else if (hitShift < lowerBound) { hitShift = -0.5f; }
            // ...and then spread/shift it to [-0.5, 0.5]
            else { hitShift = (hitShift - lowerBound) / (upperBound - lowerBound) - 0.5f; }

            //tactsuitVr.LOG("Relative x-z-position: " + relativeHitDir.x.ToString() + " "  + relativeHitDir.z.ToString());
            //tactsuitVr.LOG("HitAngle: " + hitAngle.ToString());
            //tactsuitVr.LOG("HitShift: " + hitShift.ToString());

            // No tuple returns available in .NET < 4.0, so this is the easiest quickfix
            return new KeyValuePair<float, float>(myRotation, hitShift);
        }


        [HarmonyPatch(typeof(Fighter), "TakeHit", new Type[] { typeof(float), typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool) })]
        public class bhaptics_TakeHit
        {
            [HarmonyPostfix]
            public static void Postfix(Fighter __instance, float damage, Vector3 point, Vector3 normal, bool extraDamage, bool doublehit)
            {
                if (!__instance.isHost) return;
                if (__instance.health <= 0.25f * __instance.maxHealth) tactsuitVr.StartHeartBeat();
                else { tactsuitVr.StopHeartBeat(); }
                Transform myPlayer = __instance.transform;
                var angleShift = getAngleAndShift(myPlayer, point);
                tactsuitVr.PlayBackHit("Impact", angleShift.Key, angleShift.Value);
            }
        }

        [HarmonyPatch(typeof(FighterCollision), "RecordHit", new Type[] { typeof(int), typeof(float), typeof(Vector3), typeof(Vector3), typeof(bool) })]
        public class bhaptics_CollisionRecordHit
        {
            [HarmonyPostfix]
            public static void Postfix(FighterCollision __instance, int frame, float dmg, Vector3 localPos, Vector3 localNormal, bool replayCalc)
            {
                if (!__instance.fighter.isHost) return;
                if (__instance.type == FighterCollisionType.Head) tactsuitVr.PlaybackHaptics("HitInTheFace");
            }
        }

        /*
        [HarmonyPatch(typeof(RangedAttack), "ShootProjectile", new Type[] { typeof(ProjectileFireData) })]
        public class bhaptics_ShootProjectile
        {
            [HarmonyPostfix]
            public static void Postfix(RangedAttack __instance)
            {
                bool isRightHand = false;
                if (__instance.w.MainHand.controller == TButt.TBInput.Controller.RHandController) isRightHand = true;
                if (__instance.w.testingOffHand) isRightHand = !isRightHand;
                //if (!__instance.isHost) return;
                tactsuitVr.Recoil("Blade", isRightHand);
            }
        }
        */
        [HarmonyPatch(typeof(Weapon), "RumblePulse", new Type[] { typeof(float), typeof(float) })]
        public class bhaptics_RumblePulse
        {
            [HarmonyPostfix]
            public static void Postfix(Weapon __instance, float strength, float length)
            {
                if (!__instance.fighter.isHost) return;
                if (hitTimer.ElapsedMilliseconds <= 1000) return;
                else { hitTimer.Restart(); }
                bool isRightHand = false;
                bool twoHanded = false;
                if (__instance.MainHand.controller == TButt.TBInput.Controller.RHandController) isRightHand = true;
                if (__instance.testingOffHand) twoHanded = true;
                //if ((strength == 0.75f) && (length == 0.2f)) { tactsuitVr.PlaybackHaptics("ChargeBlade"); return; }
                tactsuitVr.Recoil("Blade", isRightHand, twoHanded);
                //tactsuitVr.LOG("S: " + strength.ToString());
                //tactsuitVr.LOG("L: " + length.ToString());
                //tactsuitVr.LOG(" ");
            }
        }

    }
}
