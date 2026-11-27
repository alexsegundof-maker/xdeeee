using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Composta.Core.Exportacion
{
    // Archivo ZIP mínimo con entradas "almacenadas" (sin compresión). Es todo lo que necesita un .xlsx
    // y no depende de bibliotecas nativas de compresión, que no están garantizadas en todas las
    // plataformas de Unity (IL2CPP en Android). Formato: PKWARE APPNOTE 6.3, nombres en UTF-8.
    public sealed class ZipSinCompresion
    {
        const uint FirmaLocal = 0x04034b50, FirmaCentral = 0x02014b50, FirmaFin = 0x06054b50;
        const ushort Version = 20, BanderaUtf8 = 0x0800;

        readonly MemoryStream salida = new MemoryStream();
        readonly BinaryWriter w;
        readonly List<(byte[] nombre, uint crc, uint tam, uint posicion)> entradas = new List<(byte[], uint, uint, uint)>();
        readonly HashSet<string> nombres = new HashSet<string>(StringComparer.Ordinal);
        readonly ushort horaDos, fechaDos;
        bool terminado;

        public ZipSinCompresion(DateTime fechaLocal)
        {
            w = new BinaryWriter(salida, Encoding.UTF8);
            if (fechaLocal.Year < 1980) fechaLocal = new DateTime(1980, 1, 1);
            if (fechaLocal.Year > 2107) fechaLocal = new DateTime(2107, 12, 31);
            horaDos = (ushort)((fechaLocal.Hour << 11) | (fechaLocal.Minute << 5) | (fechaLocal.Second / 2));
            fechaDos = (ushort)(((fechaLocal.Year - 1980) << 9) | (fechaLocal.Month << 5) | fechaLocal.Day);
        }

        public void Agregar(string ruta, byte[] datos)
        {
            if (terminado) throw new InvalidOperationException("El archivo ZIP ya se cerró");
            if (string.IsNullOrEmpty(ruta) || ruta.StartsWith("/", StringComparison.Ordinal) || ruta.Contains("..") || ruta.Contains("\\"))
                throw new ArgumentException("Ruta de entrada ZIP no válida: " + ruta, nameof(ruta));
            if (!nombres.Add(ruta)) throw new ArgumentException("Entrada ZIP repetida: " + ruta, nameof(ruta));
            datos = datos ?? new byte[0];
            if (salida.Length + datos.LongLength > uint.MaxValue - 1024) throw new InvalidOperationException("El archivo supera 4 GB");

            byte[] nombre = Encoding.UTF8.GetBytes(ruta);
            uint crc = Crc32.Calcular(datos);
            uint posicion = (uint)salida.Position;

            w.Write(FirmaLocal);
            w.Write(Version);
            w.Write(BanderaUtf8);
            w.Write((ushort)0);                 // método: almacenado
            w.Write(horaDos);
            w.Write(fechaDos);
            w.Write(crc);
            w.Write((uint)datos.Length);        // tamaño comprimido
            w.Write((uint)datos.Length);        // tamaño original
            w.Write((ushort)nombre.Length);
            w.Write((ushort)0);                 // campo extra
            w.Write(nombre);
            w.Write(datos);
            entradas.Add((nombre, crc, (uint)datos.Length, posicion));
        }

        public void Agregar(string ruta, string texto) => Agregar(ruta, new UTF8Encoding(false).GetBytes(texto ?? ""));

        public byte[] Terminar()
        {
            if (!terminado)
            {
                uint inicioDirectorio = (uint)salida.Position;
                foreach (var e in entradas)
                {
                    w.Write(FirmaCentral);
                    w.Write(Version);               // creado por
                    w.Write(Version);               // necesario para extraer
                    w.Write(BanderaUtf8);
                    w.Write((ushort)0);
                    w.Write(horaDos);
                    w.Write(fechaDos);
                    w.Write(e.crc);
                    w.Write(e.tam);
                    w.Write(e.tam);
                    w.Write((ushort)e.nombre.Length);
                    w.Write((ushort)0);             // extra
                    w.Write((ushort)0);             // comentario
                    w.Write((ushort)0);             // disco
                    w.Write((ushort)0);             // atributos internos
                    w.Write((uint)0);               // atributos externos
                    w.Write(e.posicion);
                    w.Write(e.nombre);
                }
                uint tamDirectorio = (uint)salida.Position - inicioDirectorio;
                if (entradas.Count > ushort.MaxValue) throw new InvalidOperationException("Demasiadas entradas ZIP");
                w.Write(FirmaFin);
                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write((ushort)entradas.Count);
                w.Write((ushort)entradas.Count);
                w.Write(tamDirectorio);
                w.Write(inicioDirectorio);
                w.Write((ushort)0);                 // comentario
                w.Flush();
                terminado = true;
            }
            return salida.ToArray();
        }
    }

    public static class Crc32
    {
        static readonly uint[] Tabla = CrearTabla();

        static uint[] CrearTabla()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        public static uint Calcular(byte[] datos)
        {
            uint c = 0xFFFFFFFFu;
            foreach (byte b in datos) c = Tabla[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
