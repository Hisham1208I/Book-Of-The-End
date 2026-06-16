using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services.Ntfs;

/// <summary>Decodes the compressed data-run list of a non-resident NTFS attribute.</summary>
public static class DataRunParser
{
    public static List<DataRun> Parse(byte[] buffer, int offset, int maxOffset)
    {
        var runs = new List<DataRun>();
        long currentLcn = 0;
        int pos = offset;

        while (pos < maxOffset)
        {
            byte header = buffer[pos++];
            if (header == 0) break;

            int lengthBytes = header & 0x0F;
            int offsetBytes = (header >> 4) & 0x0F;
            if (lengthBytes == 0 || pos + lengthBytes + offsetBytes > maxOffset) break;

            long runLength = ReadUnsigned(buffer, pos, lengthBytes);
            pos += lengthBytes;

            if (offsetBytes == 0)
            {
                // Sparse run: no allocated clusters.
                runs.Add(new DataRun(-1, runLength));
                continue;
            }

            long runOffset = ReadSigned(buffer, pos, offsetBytes);
            pos += offsetBytes;

            currentLcn += runOffset;
            runs.Add(new DataRun(currentLcn, runLength));
        }

        return runs;
    }

    private static long ReadUnsigned(byte[] buffer, int pos, int count)
    {
        long value = 0;
        for (int i = 0; i < count; i++)
            value |= (long)buffer[pos + i] << (8 * i);
        return value;
    }

    private static long ReadSigned(byte[] buffer, int pos, int count)
    {
        long value = ReadUnsigned(buffer, pos, count);
        // Sign-extend from the top bit of the most significant byte.
        long signBit = 1L << (8 * count - 1);
        if ((value & signBit) != 0)
            value |= -(1L << (8 * count));
        return value;
    }
}
