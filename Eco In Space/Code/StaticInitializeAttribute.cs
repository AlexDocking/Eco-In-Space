namespace EcoInSpace
{
    using System;

    /// <summary>
    /// OxygenPlugin will search for static fields of any type with this attribute
    /// </summary>

    [System.AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class StaticInitializeAttribute : Attribute
    {
    }
}