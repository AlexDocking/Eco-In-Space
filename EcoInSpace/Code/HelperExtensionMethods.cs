namespace EcoInSpace
{
    using Eco.Gameplay.Objects;
    using Eco.Gameplay.Players;
    using Eco.Shared.Math;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public static class HelperExtensionMethods
    {
        public static IEnumerable<T> FindWorldObjects<T>() where T : WorldObject
        {
            return new WorldObjectManager().All.Where(worldObject => worldObject is T).Cast<T>();
        }
        public static Tuple<Vector3i, Vector3i> ForwardBack(this Axis axis)
        {
            Vector3i pos1 = default;
            Vector3i pos2 = default;
            switch (axis)
            {
                case Axis.X:
                    pos1 = Vector3i.Left;
                    pos2 = Vector3i.Right;
                    break;
                case Axis.Y:
                    pos1 = Vector3i.Down;
                    pos2 = Vector3i.Up;
                    break;
                case Axis.Z:
                    pos1 = Vector3i.Forward;
                    pos2 = Vector3i.Back;
                    break;
            }
            return new Tuple<Vector3i, Vector3i>(pos1, pos2);
        }
        /// <summary>
        /// Stomach.BurnCalories can go negative if too many calories are taken. User.TryDamage won't show the damage effect if the user is out of calories. This solves both of those issues
        /// </summary>
        /// <param name="stomach"></param>
        /// <param name="amount"></param>
        /// <param name="damage"></param>
        public static void LoseCalories(this Stomach stomach, float amount, bool damage = false)
        {
            if (damage)
            {
                if (stomach.Calories > 0)
                {
                    stomach.Owner.TryDamage(null, 0);
                }
                else
                {
                    stomach.ForceSetCalories(amount);
                    stomach.Owner.Tick();
                    stomach.Owner.TryDamage(null, 0);
                }
            }
            //false means don't use calorie modifiers
            stomach.BurnCalories(Math.Min(stomach.Calories, amount), false);
        }
        public static string RepeatString(this string str, int count)
        {
            if (count <= 0)
            {
                return "";
            }
            return string.Concat(Enumerable.Repeat(str, count));
        }
        /// <summary>
        /// Round a float down to the given number of decimal places
        /// </summary>
        /// <param name="num"></param>
        /// <param name="decimalPlaces"></param>
        /// <returns></returns>
        public static float RoundDown(this float num, int decimalPlaces)
        {
            return (float)(Math.Floor(num * Math.Pow(10, decimalPlaces)) / Math.Pow(10, decimalPlaces));
        }
        public static float RoundUp(this float num, int decimalPlaces)
        {
            return (float)(Math.Ceiling(num * Math.Pow(10, decimalPlaces)) / Math.Pow(10, decimalPlaces));
        }
        public static float SecondsSince(this DateTime pastTime)
        {
            return pastTime.SecondsUntil(DateTime.Now);
        }
        /// <summary>
        /// The number of seconds between two timestamps.
        /// </summary>
        /// <param name="pastTime">Start time</param>
        /// <param name="nowTime">End time. DateTime.Now if not given</param>
        /// <returns></returns>
        public static float SecondsUntil(this DateTime pastTime, DateTime nowTime)
        {
            return (float)(nowTime - pastTime).TotalSeconds;
        }
    }
}