using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Probe;

// Brute-force printable-run extraction from IL2CPP metadata / Unity serialized files.
// Order-preserving output matters: IL2CPP stores string literals roughly in the order they
// appear in the compiled assembly, so adjacent literals reveal which literals belong to
// which method (e.g. a console command word sits next to its description string).
internal static class Strings
{
    public static void Run(string outDir)
    {
        const string gameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Schedule I";
        Literals(Path.Combine(gameDir, @"Schedule I_Data\il2cpp_data\Metadata\global-metadata.dat"), outDir);
        var jobs = new (string file, string outName, int minLen)[]
        {
            (Path.Combine(gameDir, @"Schedule I_Data\il2cpp_data\Metadata\global-metadata.dat"), "strings-global-metadata", 4),
            (Path.Combine(gameDir, @"Schedule I_Data\globalgamemanagers"), "strings-globalgamemanagers", 4),
            (Path.Combine(gameDir, @"Schedule I_Data\boot.config"), "strings-bootconfig", 3),
        };

        foreach (var (file, outName, minLen) in jobs)
        {
            if (!File.Exists(file)) { Console.WriteLine("missing " + file); continue; }
            var bytes = File.ReadAllBytes(file);
            var ordered = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if (b >= 0x20 && b < 0x7F) sb.Append((char)b);
                else
                {
                    if (sb.Length >= minLen) ordered.Add(sb.ToString());
                    sb.Clear();
                }
            }
            if (sb.Length >= minLen) ordered.Add(sb.ToString());

            File.WriteAllLines(Path.Combine(outDir, outName + "-ordered.txt"), ordered);
            var uniq = new SortedSet<string>(ordered, StringComparer.Ordinal);
            File.WriteAllLines(Path.Combine(outDir, outName + "-sorted.txt"), uniq);
            Console.WriteLine($"{outName}: {ordered.Count} runs, {uniq.Count} unique");
        }
    }

    // Il2CppGlobalMetadataHeader (metadata v29 / Unity 2022.3):
    //   0  uint32 sanity (0xFAB11BAF)
    //   4  int32  version
    //   8  int32  stringLiteralOffset      (table of Il2CppStringLiteral { uint32 length; int32 dataIndex; })
    //   12 int32  stringLiteralSize        (bytes)
    //   16 int32  stringLiteralDataOffset  (raw UTF-8 blob, no terminators)
    //   20 int32  stringLiteralDataSize
    //   24 int32  stringOffset             (null-terminated identifier strings)
    //   28 int32  stringSize
    static void Literals(string file, string outDir)
    {
        if (!File.Exists(file)) { Console.WriteLine("missing " + file); return; }
        var b = File.ReadAllBytes(file);
        uint sanity = BitConverter.ToUInt32(b, 0);
        int version = BitConverter.ToInt32(b, 4);
        int litOff = BitConverter.ToInt32(b, 8);
        int litSize = BitConverter.ToInt32(b, 12);
        int datOff = BitConverter.ToInt32(b, 16);
        int datSize = BitConverter.ToInt32(b, 20);
        Console.WriteLine($"metadata sanity=0x{sanity:X8} version={version} literals={litSize / 8} dataSize={datSize}");
        if (sanity != 0xFAB11BAFu) { Console.WriteLine("!! bad sanity, aborting literal parse"); return; }

        var res = new List<string>(litSize / 8);
        for (int i = 0; i + 8 <= litSize; i += 8)
        {
            uint len = BitConverter.ToUInt32(b, litOff + i);
            int di = BitConverter.ToInt32(b, litOff + i + 4);
            if (len > 1_000_000 || di < 0 || datOff + di + len > b.Length) { res.Add("<<bad literal>>"); continue; }
            res.Add(Escape(Encoding.UTF8.GetString(b, datOff + di, (int)len)));
        }
        // Prefix each line with its 1-based literal index so a grep hit can be located again.
        File.WriteAllLines(Path.Combine(outDir, "literals-ordered.txt"), res);
        var u = new SortedSet<string>(res, StringComparer.Ordinal);
        File.WriteAllLines(Path.Combine(outDir, "literals-sorted.txt"), u);
        Console.WriteLine($"literals: {res.Count} ordered, {u.Count} unique");
    }

    // Some game string literals legitimately contain control bytes (tabs, newlines, rich-text
    // control chars). Left raw they make editors and file readers classify the dump as binary and
    // refuse to display it, so every non-printable char is escaped to a visible \xNN form.
    static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '\r') sb.Append("\\r");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\t') sb.Append("\\t");
            else if (c < 0x20 || c == 0x7F) sb.Append("\\x").Append(((int)c).ToString("X2"));
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
