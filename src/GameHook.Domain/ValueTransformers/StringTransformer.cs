﻿namespace GameHook.Domain.ValueTransformers
{
    public static class StringTransformer
    {
        public static byte[] FromValue(string value, int size, GlossaryList referenceList)
        {
            var uints = value
                .Select(x => referenceList.Values.FirstOrDefault(y => x.ToString() == y?.Value?.ToString()))
                .ToList();

            if (uints.Count + 1 > size)
            {
                uints = uints.Take(size - 1).ToList();
            }

            var nullTerminationKey = referenceList.Values.First(x => x.Value == null);
            uints.Add(nullTerminationKey);

            return uints
                .Select(x =>
                {
                    if (x?.Value == null)
                    {
                        return nullTerminationKey.Key;
                    }

                    return x.Key;
                })
                .Select(x => (byte)x)
                .ToArray();
        }

        public static string ToValue(byte[] data, GlossaryList referenceList)
        {
            var results = data.Select(b =>
            {
                var referenceItem = referenceList.Values.SingleOrDefault(x => x.Key == b);
                return referenceItem?.Value?.ToString() ?? null;
            });

            // Return the completed string buffer.
            return string.Join(string.Empty, results.TakeWhile(s => s != null));
        }
    }
}