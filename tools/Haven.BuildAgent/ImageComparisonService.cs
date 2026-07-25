using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;

namespace Haven.BuildAgent;

public sealed class ImageComparisonService
{
    private readonly BuildAgentOptions _options;

    public ImageComparisonService(IOptions<BuildAgentOptions> options)
    {
        _options = options.Value;
    }

    public PixelComparisonResult Compare(string actualPath, string referencePath, int threshold)
    {
        int normalizedThreshold = Math.Clamp(threshold, 0, 255);
        using var actualSource = new Bitmap(actualPath);
        using var referenceSource = new Bitmap(referencePath);
        bool dimensionsMatch = actualSource.Width == referenceSource.Width && actualSource.Height == referenceSource.Height;
        using Bitmap actual = ConvertToArgb(actualSource, actualSource.Width, actualSource.Height);
        using Bitmap reference = ConvertToArgb(referenceSource, actual.Width, actual.Height);
        using var difference = new Bitmap(actual.Width, actual.Height, PixelFormat.Format32bppArgb);

        Rectangle bounds = new(0, 0, actual.Width, actual.Height);
        BitmapData actualData = actual.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData referenceData = reference.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData differenceData = difference.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        long totalDelta = 0;
        long changedPixels = 0;
        int minimumX = actual.Width;
        int minimumY = actual.Height;
        int maximumX = -1;
        int maximumY = -1;

        try
        {
            int actualStride = Math.Abs(actualData.Stride);
            int referenceStride = Math.Abs(referenceData.Stride);
            int differenceStride = Math.Abs(differenceData.Stride);
            byte[] actualBytes = new byte[actualStride * actual.Height];
            byte[] referenceBytes = new byte[referenceStride * reference.Height];
            byte[] differenceBytes = new byte[differenceStride * difference.Height];
            Marshal.Copy(actualData.Scan0, actualBytes, 0, actualBytes.Length);
            Marshal.Copy(referenceData.Scan0, referenceBytes, 0, referenceBytes.Length);

            for (int y = 0; y < actual.Height; y++)
            {
                for (int x = 0; x < actual.Width; x++)
                {
                    int actualIndex = (y * actualStride) + (x * 4);
                    int referenceIndex = (y * referenceStride) + (x * 4);
                    int differenceIndex = (y * differenceStride) + (x * 4);

                    int blueDifference = Math.Abs(actualBytes[actualIndex] - referenceBytes[referenceIndex]);
                    int greenDifference = Math.Abs(actualBytes[actualIndex + 1] - referenceBytes[referenceIndex + 1]);
                    int redDifference = Math.Abs(actualBytes[actualIndex + 2] - referenceBytes[referenceIndex + 2]);
                    int maximumDifference = Math.Max(blueDifference, Math.Max(greenDifference, redDifference));
                    totalDelta += blueDifference + greenDifference + redDifference;

                    differenceBytes[differenceIndex] = (byte)maximumDifference;
                    differenceBytes[differenceIndex + 1] = (byte)maximumDifference;
                    differenceBytes[differenceIndex + 2] = (byte)maximumDifference;
                    differenceBytes[differenceIndex + 3] = byte.MaxValue;

                    if (maximumDifference <= normalizedThreshold)
                    {
                        continue;
                    }

                    changedPixels++;
                    minimumX = Math.Min(minimumX, x);
                    minimumY = Math.Min(minimumY, y);
                    maximumX = Math.Max(maximumX, x);
                    maximumY = Math.Max(maximumY, y);
                }
            }

            Marshal.Copy(differenceBytes, 0, differenceData.Scan0, differenceBytes.Length);
        }
        finally
        {
            actual.UnlockBits(actualData);
            reference.UnlockBits(referenceData);
            difference.UnlockBits(differenceData);
        }

        string comparisonDirectory = _options.CreateArtifactDirectory("comparisons", Guid.NewGuid());
        string differencePath = Path.Combine(comparisonDirectory, "difference.png");
        difference.Save(differencePath, ImageFormat.Png);

        long pixelCount = (long)actual.Width * actual.Height;
        double similarity = pixelCount == 0
            ? 0
            : 100 * (1 - (totalDelta / (pixelCount * 3d * byte.MaxValue)));
        double changedPercentage = pixelCount == 0 ? 0 : 100d * changedPixels / pixelCount;
        DifferenceBounds? differenceBounds = changedPixels == 0
            ? null
            : new DifferenceBounds(minimumX, minimumY, maximumX, maximumY);

        return new PixelComparisonResult(
            Math.Clamp(similarity, 0, 100),
            Math.Clamp(changedPercentage, 0, 100),
            dimensionsMatch,
            actual.Width,
            actual.Height,
            referenceSource.Width,
            referenceSource.Height,
            normalizedThreshold,
            differenceBounds,
            _options.ToArtifactUrl(differencePath));
    }

    private static Bitmap ConvertToArgb(Image source, int width, int height)
    {
        var converted = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(converted);
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return converted;
    }
}
