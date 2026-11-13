using System;
using System.Net;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Frostline.Remote
{
    /// <summary>
    /// Meldet das Spiel im WLAN per Bonjour an, damit die iPhone-App den Mac ohne
    /// IP-Eingabe findet. Nutzt direkt die dns_sd-Schnittstelle von macOS; auf anderen
    /// Systemen passiert nichts und die App braucht die IP-Adresse.
    /// </summary>
    public sealed class Bonjour : IDisposable
    {
        IntPtr handle = IntPtr.Zero;

        public bool Registered { get { return handle != IntPtr.Zero; } }

        public static Bonjour Register(string serviceType, int port)
        {
            var bonjour = new Bonjour();
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            try
            {
                ushort networkPort = (ushort)IPAddress.HostToNetworkOrder((short)port);
                // Name null = Gerätename des Macs; Rückmeldung wird nicht gebraucht.
                int error = DNSServiceRegister(out bonjour.handle, 0, 0, null, serviceType, null, null,
                    networkPort, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (error != 0)
                {
                    bonjour.handle = IntPtr.Zero;
                    Debug.LogWarning("Frostline: Bonjour-Anmeldung fehlgeschlagen (Fehler " + error + "). Die App braucht dann die IP-Adresse.");
                }
            }
            catch (Exception e)
            {
                bonjour.handle = IntPtr.Zero;
                Debug.LogWarning("Frostline: Bonjour nicht verfügbar: " + e.Message);
            }
#endif
            return bonjour;
        }

        public void Dispose()
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (handle != IntPtr.Zero)
            {
                try { DNSServiceRefDeallocate(handle); }
                catch (Exception) { }
            }
#endif
            handle = IntPtr.Zero;
        }

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        const string Library = "/usr/lib/libSystem.dylib";

        [DllImport(Library, CharSet = CharSet.Ansi)]
        static extern int DNSServiceRegister(out IntPtr sdRef, uint flags, uint interfaceIndex,
            string name, string regType, string domain, string host, ushort port,
            ushort txtLength, IntPtr txtRecord, IntPtr callBack, IntPtr context);

        [DllImport(Library)]
        static extern void DNSServiceRefDeallocate(IntPtr sdRef);
#endif
    }
}
