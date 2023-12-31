﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ImageProcessor
{
    public partial class MainWindow : Window
    {
        private BitmapImage? processedImage; 

        public MainWindow()
        {
            InitializeComponent();
            this.saveOutput.IsEnabled = false;
        }

        private void ProcessImage_Click(object sender, RoutedEventArgs e)
        {
            this.afterImage.Source = null;
            this.processedImage = null;
            this.saveOutput.IsEnabled = false;
            this.Log.Clear();

            OpenFileDialog fileDialog = new OpenFileDialog();
            if (fileDialog.ShowDialog() == true)
            {
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(fileDialog.FileName, UriKind.RelativeOrAbsolute);
                bi.EndInit();

                beforeImage.Source = bi;

                this.statusBox.Text = "Processing ...";

                BackgroundWorker worker = new BackgroundWorker();
                worker.DoWork += Worker_DoWork;
                worker.RunWorkerCompleted += WorkCallback;
                worker.RunWorkerAsync(fileDialog.FileName);

            }
        }

        private void Worker_DoWork(object? sender, DoWorkEventArgs e)
        {
            if (e.Argument is string argument)
            {
                var result = this.ProcessImage(argument);
                e.Result = result;
            }
        }

        private void WorkCallback(object? sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Result is BitmapImage image)
            {
                afterImage.Source = image;
                this.statusBox.Text = string.Format("Done, Last Run at: {0:HH:mm:ss}", DateTime.Now);
                this.saveOutput.IsEnabled = true;
                processedImage = image;
            }
        }
        
        private BitmapImage? ProcessImage(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return null;
            }

            Stopwatch watch = Stopwatch.StartNew();
            Bitmap bmp = new Bitmap(filename);

            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);

            IntPtr ptr = bmpData.Scan0; // First Line

            int bytes = bmpData.Stride * bmp.Height;
            byte[] rgbValues = new byte[bytes];
            Marshal.Copy(ptr, rgbValues, 0, bytes);

            int stride = bmpData.Stride;

            LogMessage(string.Format("Source Image: {0}x{1}", bmpData.Width, bmpData.Height));

            List<byte> outputImage = new List<byte>();
            int rowsRemoved = 0;

            int expectedRowBytes = bmpData.Width * 3;

            for (int row = 0; row < bmpData.Height; row++)
            {

                bool rowDisplayed = false;
                if (row % 20 == 0)
                {
                    rowDisplayed = true;
                    LogMessage("Processing Row: " + row); // Don't hit the WPF textbox so hard. It slows things down.
                }

                bool isBlackRow = true;
                List<byte> rowBytes = new List<byte>();
                for (int column = 0; column < bmpData.Width; column++)
                {
                    byte r = rgbValues[(row * stride) + (column * 3)];
                    byte g = rgbValues[(row * stride) + (column * 3) + 1];
                    byte b = rgbValues[(row * stride) + (column * 3) + 2];

                    if (!IsBlackPixel(r, g, b))
                    {
                        isBlackRow = false;

                        rowBytes.Add(r);
                        rowBytes.Add(g);
                        rowBytes.Add(b);
                    }
                }

                if (rowBytes.Count < expectedRowBytes)
                {
                    if (!rowDisplayed)
                    {
                        LogMessage("Processing Row:" + row);
                    }

                    rowsRemoved += 1;
                    LogMessage("- Row has less bytes than expected. ");
                }
                else if (isBlackRow)
                {
                    if (!rowDisplayed)
                    {
                        LogMessage("Processing Row:" + row);
                    }

                    rowsRemoved += 1;
                    LogMessage("- Row is black");
                }
                else
                {
                    outputImage.AddRange(rowBytes); // Add the non black row.
                }
            }

            watch.Stop();
            LogMessage($"Processing Complete! Took {Math.Round((double)watch.ElapsedMilliseconds / 1000, 2)} s");

            ThreadHelper.OnUIThread(() =>
            {
                this.LinesRemoved.Text = "Processed Image, Rows Removed: " + rowsRemoved;
            });
      
            return CreateFinalBitmap(outputImage.ToArray(), bmpData.Width, (bmpData.Height - rowsRemoved));
        }

        private BitmapImage? CreateFinalBitmap(byte[] buffer, int width, int height)
        {
            Bitmap b = new Bitmap(width, height, PixelFormat.Format24bppRgb);

            Rectangle BoundsRect = new Rectangle(0, 0, width, height);
            BitmapData bmpData = b.LockBits(BoundsRect, ImageLockMode.WriteOnly, b.PixelFormat);

            IntPtr ptr = bmpData.Scan0;

            int skipByte = bmpData.Stride - width * 3;
            byte[] newBuff = new byte[buffer.Length + skipByte * height];
            for (int j = 0; j < height; j++)
            {
                Buffer.BlockCopy(buffer, j * width * 3, newBuff, j * (width * 3 + skipByte), width * 3);
            }

            Marshal.Copy(newBuff, 0, ptr, newBuff.Length);
            b.UnlockBits(bmpData);

            BitmapImage? result = ToBitmapImage(b);
            return result;
        }

        public static BitmapImage? ToBitmapImage(Bitmap bitmap)
        {
            using (var memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png);
                memory.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
        }

        private bool IsBlackPixel(byte r, byte g, byte b)
        {
            if (r == 0 && g == 0 && b == 0)
            {
                return true;
            }

            return false;
        }

        private void LogMessage(string message)
        {
            ThreadHelper.OnUIThread(() =>
            {
                Debug.WriteLine(message);
                this.Log.AppendText(string.Format("[{0}] {1} {2}", DateTime.Now.ToString("HH:mm:ss"), message, Environment.NewLine));
                this.Log.ScrollToEnd();
            });
        }

        private void SaveOutput_OnClick(object sender, RoutedEventArgs e)
        {
            if (processedImage != null)
            {
                SaveFileDialog fileDialog = new SaveFileDialog();
                fileDialog.Filter = "PNG|*.png|JPG|*.jpg";
                fileDialog.DefaultExt = "PNG|*.png";
                if (fileDialog.ShowDialog() == true)
                {
                    var extension = Path.GetExtension(fileDialog.FileName);

                    BitmapEncoder encoder;
                    switch (extension.ToLower())
                    {
                        case ".jpg":
                            encoder = new JpegBitmapEncoder();
                            break;
                        case ".png":
                            encoder = new PngBitmapEncoder();
                            break;
                        default:
                            MessageBox.Show("Invalid File Extension. Aborting.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                    }

                    encoder.Frames.Add(BitmapFrame.Create(processedImage));

                    using (var fileStream = new FileStream(fileDialog.FileName, FileMode.Create))
                    {
                        encoder.Save(fileStream);
                        LogMessage("File Saved: " + fileDialog.FileName);
                    }
                }
            }

        }
    }
}
