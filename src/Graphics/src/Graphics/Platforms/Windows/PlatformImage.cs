﻿using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.IO;
using Windows.Storage.Streams;

#if MAUI_GRAPHICS_WIN2D
namespace Microsoft.Maui.Graphics.Win2D
#else
namespace Microsoft.Maui.Graphics.Platform
#endif
{
	/// <summary>
	/// A Windows platform implementation of <see cref="IImage"/>.
	/// </summary>
#if MAUI_GRAPHICS_WIN2D
	internal class W2DImage
#else
	public class PlatformImage
#endif
		: IImage
	{
		private readonly ICanvasResourceCreator _creator;
		private CanvasBitmap _bitmap;

		private static readonly RecyclableMemoryStreamManager recyclableMemoryStreamManager = new();

#if MAUI_GRAPHICS_WIN2D
		public W2DImage(
#else
		public PlatformImage(
#endif
			ICanvasResourceCreator creator, CanvasBitmap bitmap)
		{
			_creator = creator;
			_bitmap = bitmap;
		}

		public CanvasBitmap PlatformRepresentation => _bitmap;

		public void Dispose()
		{
			var bitmap = Interlocked.Exchange(ref _bitmap, null);
			bitmap?.Dispose();
		}

		public IImage Downsize(float maxWidthOrHeight, bool disposeOriginal = false)
		{
			if (Width > maxWidthOrHeight || Height > maxWidthOrHeight)
			{
				float factor = Width > Height ? maxWidthOrHeight / Width : maxWidthOrHeight / Height;
				var targetWidth = factor * Width;
				var targetHeight = factor * Height;
				return ResizeInternal(targetWidth, targetHeight, 0, 0, targetWidth, targetHeight, disposeOriginal);
			}

			return this;
		}

		public IImage Downsize(float maxWidth, float maxHeight, bool disposeOriginal = false)
		{
			return ResizeInternal(maxWidth, maxHeight, 0, 0, maxWidth, maxHeight, disposeOriginal);
		}


		IImage ResizeInternal(float canvasWidth, float canvasHeight, float drawX, float drawY, float drawWidth, float drawHeight, bool disposeOriginal)
		{
			using var renderTarget = new CanvasRenderTarget(_creator, canvasWidth, canvasHeight, _bitmap.Dpi);

			using (var drawingSession = renderTarget.CreateDrawingSession())
			{
				drawingSession.DrawImage(_bitmap, new global::Windows.Foundation.Rect(drawX, drawY, drawWidth, drawHeight));
			}

			using (var resizedStream = new InMemoryRandomAccessStream())
			{
				var saveCompletedEvent = new ManualResetEventSlim(false);
				Exception saveException = null;

				// Start the async save operation
				var saveTask = renderTarget.SaveAsync(resizedStream, CanvasBitmapFileFormat.Png).AsTask();

				saveTask.ContinueWith(task =>
				{
					if (task.Exception is not null)
					{
						saveException = task.Exception;
					}
					// Signal that the operation is complete
					saveCompletedEvent.Set();
				});

				// Wait for the signal
				saveCompletedEvent.Wait();

				// Check for any exceptions during the async operation
				if (saveException is not null)
				{
					throw saveException;
				}

				resizedStream.Seek(0);

				var newImage = FromStream(resizedStream.AsStreamForRead());

				if (disposeOriginal)
				{
					_bitmap.Dispose();
				}

				return newImage;
			}
		}

		public IImage Resize(float width, float height, ResizeMode resizeMode = ResizeMode.Fit,
			bool disposeOriginal = false)
		{
			// Calculate scaling factors
			float scaleX = width / Width;
			float scaleY = height / Height;

			float targetWidth = Width;
			float targetHeight = Height;
			float offsetX = 0;
			float offsetY = 0;

			// Adjust dimensions based on the resize mode
			if (resizeMode == ResizeMode.Fit)
			{
				float scale = Math.Min(scaleX, scaleY);
				targetWidth *= scale;
				targetHeight *= scale;
				offsetX = (width - targetWidth) / 2;
				offsetY = (height - targetHeight) / 2;
			}
			else if (resizeMode == ResizeMode.Bleed)
			{
				float scale = Math.Max(scaleX, scaleY);
				targetWidth *= scale;
				targetHeight *= scale;
				offsetX = (width - targetWidth) / 2;
				offsetY = (height - targetHeight) / 2;
			}
			else
			{
				targetWidth = width;
				targetHeight = height;
			}

			return ResizeInternal(width, height, offsetX, offsetY, targetWidth, targetHeight, disposeOriginal);
		}

		public float Width => (float)_bitmap.Size.Width;

		public float Height => (float)_bitmap.Size.Height;

		/// <summary>
		/// Saves the contents of this image to the provided <see cref="Stream"/> object.
		/// </summary>
		/// <param name="stream">The destination stream the bytes of this image will be saved to.</param>
		/// <param name="format">The destination format of the image.</param>
		/// <param name="quality">The destination quality of the image.</param>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="quality"/> is less than 0 or more than 1.</exception>
		/// <remarks>
		/// <para>Only <see cref="ImageFormat.Png"/> and <see cref="ImageFormat.Jpeg"/> are supported on this platform.</para>
		/// <para>Setting <paramref name="quality"/> is only supported for images with <see cref="ImageFormat.Jpeg"/>.</para>
		/// </remarks>
		public void Save(Stream stream, ImageFormat format = ImageFormat.Png, float quality = 1)
		{
			if (quality < 0 || quality > 1)
				throw new ArgumentOutOfRangeException(nameof(quality), "quality must be in the range of 0..1");

			switch (format)
			{
				case ImageFormat.Jpeg:
					AsyncPump.Run(async () => await _bitmap.SaveAsync(stream.AsRandomAccessStream(), CanvasBitmapFileFormat.Jpeg, quality));
					break;
				default:
					AsyncPump.Run(async () => await _bitmap.SaveAsync(stream.AsRandomAccessStream(), CanvasBitmapFileFormat.Png));
					break;
			}
		}

		/// <inheritdoc cref="Save" />
		public async Task SaveAsync(Stream stream, ImageFormat format = ImageFormat.Png, float quality = 1)
		{
			if (quality < 0 || quality > 1)
				throw new ArgumentOutOfRangeException(nameof(quality), "quality must be in the range of 0..1");

			switch (format)
			{
				case ImageFormat.Jpeg:
					await _bitmap.SaveAsync(stream.AsRandomAccessStream(), CanvasBitmapFileFormat.Jpeg, quality);
					break;
				default:
					await _bitmap.SaveAsync(stream.AsRandomAccessStream(), CanvasBitmapFileFormat.Png);
					break;
			}
		}

		public void Draw(ICanvas canvas, RectF dirtyRect)
		{
			canvas.DrawImage(this, dirtyRect.Left, dirtyRect.Top, Math.Abs(dirtyRect.Width), Math.Abs(dirtyRect.Height));
		}

		public IImage ToPlatformImage()
		{
#if MAUI_GRAPHICS_WIN2D
			return new Platform.PlatformImage(_creator, _bitmap);
#else
			return this;
#endif
		}

		public IImage ToImage(int width, int height, float scale = 1f)
		{
			throw new NotImplementedException();
		}

		public static IImage FromStream(Stream stream, ImageFormat format = ImageFormat.Png)
		{
			var creator = PlatformGraphicsService.Creator;

			if (creator == null)
			{
				throw new Exception("No resource creator has been registered globally or for this thread.");
			}

			CanvasBitmap bitmap;

			if (stream.CanSeek)
			{
				var bitmapAsync = CanvasBitmap.LoadAsync(creator, stream.AsRandomAccessStream());
				bitmap = bitmapAsync.AsTask().GetAwaiter().GetResult();
			}
			else
			{
				using var memoryStream = recyclableMemoryStreamManager.GetStream();
				stream.CopyTo(memoryStream);
				memoryStream.Seek(0, SeekOrigin.Begin);

				var bitmapAsync = CanvasBitmap.LoadAsync(creator, memoryStream.AsRandomAccessStream());
				bitmap = bitmapAsync.AsTask().GetAwaiter().GetResult();
			}

			return new PlatformImage(creator, bitmap);
		}
	}
}
