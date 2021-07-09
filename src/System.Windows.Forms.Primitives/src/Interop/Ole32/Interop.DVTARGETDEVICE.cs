﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

internal static partial class Interop
{
    internal static partial class Ole32
    {
        public struct DVTARGETDEVICE
        {
            public uint tdSize;
            public ushort tdDriverNameOffset;
            public ushort tdDeviceNameOffset;
            public ushort tdPortNameOffset;
            public ushort tdExtDevmodeOffset;
        }
    }
}