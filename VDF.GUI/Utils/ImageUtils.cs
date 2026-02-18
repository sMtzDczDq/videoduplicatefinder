// /*
//     Copyright (C) 2025 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//


using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace VDF.GUI.Utils {
	static class ImageUtils {
		public static Bitmap? JoinImages(List<Image> pImgList) {
			if (pImgList == null || pImgList.Count == 0) return null;

			int height = pImgList[0].Height;
			int width = 0;
			for (int i = 0; i <= pImgList.Count - 1; i++)
				width += pImgList[i].Width;

			using var img = new Image<Rgba32>(width, height); // create output image of the correct dimensions

			List<Point> locations = new(pImgList.Count);
			int tmpwidth = 0;
			for (int i = 0; i <= pImgList.Count - 1; i++) {
				img.Mutate(a => a.DrawImage(pImgList[i], new Point(tmpwidth, 0), 1f));
				tmpwidth += pImgList[i].Width;
			}

			try {
				using var bgraImage = img.CloneAs<Bgra32>();

				if (!bgraImage.DangerousTryGetSinglePixelMemory(out var pixelMemory))
					return null;

				Span<byte> sourcePixelData = MemoryMarshal.AsBytes(pixelMemory.Span);

				var writeableBitmap = new WriteableBitmap(
					new Avalonia.PixelSize(bgraImage.Width, bgraImage.Height),
					new Avalonia.Vector(96, 96),
					PixelFormat.Bgra8888,
					AlphaFormat.Unpremul
				);

				using (var lockedFramebuffer = writeableBitmap.Lock()) {
					Span<byte> destinationSpan = new Span<byte>(
						(void*)lockedFramebuffer.Address,
						lockedFramebuffer.Size.Height * lockedFramebuffer.RowBytes
					);

					int expectedSourceLength = bgraImage.Width * bgraImage.Height * 4;

					if (sourcePixelData.Length == expectedSourceLength && destinationSpan.Length == expectedSourceLength) {
						sourcePixelData.CopyTo(destinationSpan);
					}
					else if (sourcePixelData.Length == destinationSpan.Length) {
						int sourceStride = bgraImage.Width * 4;
						int destStride = lockedFramebuffer.RowBytes;
						for (int y = 0; y < bgraImage.Height; y++) {
							Span<byte> sourceRow = sourcePixelData.Slice(y * sourceStride, sourceStride);
							Span<byte> destRow = destinationSpan.Slice(y * destStride, sourceStride);
							sourceRow.CopyTo(destRow);
						}
					}
					else {
						return null; // sizes do not fit
					}
				}

				if (jpegOut != null) {
					img.SaveAsJpeg(jpegOut, JpegEncoder);
					try { jpegOut.Flush(); } catch { /* ignore */ }
					if (jpegOut.CanSeek) { try { jpegOut.Position = 0; } catch { } }
				}

				return writeableBitmap;
			}
			catch {
				return null;
			}
		}
		public static unsafe Bitmap? JoinImages(IReadOnlyList<Bitmap> images, Stream? jpegOut = null) {
			if (images == null || images.Count == 0) return null;

			int h = images[0].PixelSize.Height;
			int w = 0; for (int i = 0; i < images.Count; i++) w += images[i].PixelSize.Width;

			RenderTargetBitmap rtb = new(new PixelSize(w, h));

			using var dc = rtb.CreateDrawingContext();
			//dc.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));

			double x = 0;
			foreach (var bmp in images) {
				var src = new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
				var dst = new Rect(x, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
				dc.DrawImage(bmp, src, dst);
				x += bmp.PixelSize.Width;
			}
			return rtb;
		}

		public static byte[] ToByteArray(this Bitmap image) {
			using MemoryStream ms = new();
			image.Save(ms);
			return ms.ToArray();
		}
		public static byte[] ToByteArray(this SixLabors.ImageSharp.Image image) {
			using MemoryStream ms = new();
			image.Save(ms, JpegEncoder);
			return ms.ToArray();
		}

	}
}
