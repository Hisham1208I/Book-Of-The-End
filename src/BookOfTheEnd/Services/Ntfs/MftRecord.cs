using System.Text;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services.Ntfs;

/// <summary>
/// Parses a single (already fixup-applied) MFT record into the fields needed for recovery.
/// </summary>
public sealed class MftRecord
{
    public bool IsValid { get; private set; }
    public bool InUse { get; private set; }
    public bool IsDirectory { get; private set; }

    public string? FileName { get; private set; }
    public byte FileNameNamespace { get; private set; } = 0xFF;

    public DateTime? Created { get; private set; }
    public DateTime? Modified { get; private set; }

    public long RealSize { get; private set; }
    public byte[]? ResidentData { get; private set; }
    public List<DataRun>? DataRuns { get; private set; }

    private const uint AttrStandardInformation = 0x10;
    private const uint AttrFileName = 0x30;
    private const uint AttrData = 0x80;
    private const uint AttrEnd = 0xFFFFFFFF;

    public static MftRecord Parse(byte[] record)
    {
        var r = new MftRecord();
        if (record.Length < 0x30) return r;
        if (record[0] != (byte)'F' || record[1] != (byte)'I' || record[2] != (byte)'L' || record[3] != (byte)'E')
            return r;

        r.IsValid = true;
        ushort flags = BitConverter.ToUInt16(record, 0x16);
        r.InUse = (flags & 0x01) != 0;
        r.IsDirectory = (flags & 0x02) != 0;

        int firstAttr = BitConverter.ToUInt16(record, 0x14);
        int usedSize = (int)BitConverter.ToUInt32(record, 0x18);
        int limit = Math.Min(record.Length, usedSize <= 0 ? record.Length : usedSize);

        int pos = firstAttr;
        while (pos + 8 <= limit)
        {
            uint type = BitConverter.ToUInt32(record, pos);
            if (type == AttrEnd) break;

            int attrLen = (int)BitConverter.ToUInt32(record, pos + 4);
            if (attrLen <= 0 || pos + attrLen > record.Length) break;

            bool nonResident = record[pos + 8] != 0;

            switch (type)
            {
                case AttrStandardInformation when !nonResident:
                    r.ReadStandardInformation(record, pos);
                    break;
                case AttrFileName when !nonResident:
                    r.ReadFileName(record, pos);
                    break;
                case AttrData:
                    r.ReadData(record, pos, nonResident, attrLen);
                    break;
            }

            pos += attrLen;
        }

        return r;
    }

    private void ReadStandardInformation(byte[] record, int attrPos)
    {
        int contentOffset = BitConverter.ToUInt16(record, attrPos + 0x14);
        int p = attrPos + contentOffset;
        if (p + 0x20 > record.Length) return;
        Created ??= ToDate(BitConverter.ToInt64(record, p + 0x00));
        Modified ??= ToDate(BitConverter.ToInt64(record, p + 0x08));
    }

    private void ReadFileName(byte[] record, int attrPos)
    {
        int contentOffset = BitConverter.ToUInt16(record, attrPos + 0x14);
        int p = attrPos + contentOffset;
        if (p + 0x42 > record.Length) return;

        long realSize = BitConverter.ToInt64(record, p + 0x30);
        byte nameLength = record[p + 0x40];
        byte nameNamespace = record[p + 0x41];
        int nameStart = p + 0x42;
        if (nameStart + nameLength * 2 > record.Length) return;

        string name = Encoding.Unicode.GetString(record, nameStart, nameLength * 2);

        // Prefer a Win32 (long) name over a DOS 8.3 alias.
        if (FileName is null || (FileNameNamespace == 2 && nameNamespace != 2))
        {
            FileName = name;
            FileNameNamespace = nameNamespace;
            if (RealSize == 0) RealSize = realSize;
        }

        // FILE_NAME timestamps are a fallback if $STANDARD_INFORMATION was missing.
        Created ??= ToDate(BitConverter.ToInt64(record, p + 0x08));
        Modified ??= ToDate(BitConverter.ToInt64(record, p + 0x10));
    }

    private void ReadData(byte[] record, int attrPos, bool nonResident, int attrLen)
    {
        // Only the unnamed $DATA stream is the primary file content.
        byte nameLength = record[attrPos + 0x09];
        if (nameLength != 0) return;

        if (!nonResident)
        {
            int contentLen = (int)BitConverter.ToUInt32(record, attrPos + 0x10);
            int contentOffset = BitConverter.ToUInt16(record, attrPos + 0x14);
            int start = attrPos + contentOffset;
            if (start + contentLen > record.Length) contentLen = Math.Max(0, record.Length - start);
            ResidentData = new byte[contentLen];
            Array.Copy(record, start, ResidentData, 0, contentLen);
            RealSize = contentLen;
        }
        else
        {
            long realSize = BitConverter.ToInt64(record, attrPos + 0x30);
            int runOffset = BitConverter.ToUInt16(record, attrPos + 0x20);
            int runStart = attrPos + runOffset;
            int runMax = attrPos + attrLen;
            if (runStart < record.Length)
            {
                DataRuns = DataRunParser.Parse(record, runStart, Math.Min(runMax, record.Length));
                RealSize = realSize;
            }
        }
    }

    private static DateTime? ToDate(long fileTime)
    {
        if (fileTime <= 0) return null;
        try { return DateTime.FromFileTimeUtc(fileTime).ToLocalTime(); }
        catch { return null; }
    }
}
