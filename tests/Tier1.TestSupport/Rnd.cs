#pragma warning disable CA5394 // بيانات تجريبية مولدة: العشوائية هنا ليست لغرض أمني.
using System.Security.Cryptography;
using System.Text;

namespace Tier1.TestSupport;

/// <summary>مولد عشوائي ثابت بمفتاح نصي: القيم نفسها في كل تشغيل.</summary>
internal sealed class Rnd(params object[] keys)
{
    private readonly Random random = new(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', keys))), 0));

    public double Uniform(double a, double b) => a + (b - a) * random.NextDouble();

    public int Int(int a, int b) => random.Next(a, b + 1);

    public double Next() => random.NextDouble();

    public T Choice<T>(params T[] items) => items[random.Next(items.Length)];

    public string Weighted(string[] items, int[] weights)
    {
        var x = random.NextDouble() * weights.Sum();
        for (var i = 0; i < items.Length; i++)
        {
            x -= weights[i];
            if (x < 0) return items[i];
        }
        return items[^1];
    }
}
