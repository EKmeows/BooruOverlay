using System;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace ImageOverlay
{
    public partial class OverlayForm : Form
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private System.Windows.Forms.Timer imageChangeTimer;
        private System.Windows.Forms.Timer styleMaintenanceTimer;
        private Random random = new Random();
        private PictureBox imagePictureBox;

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_NOACTIVATE = 0x8000000;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        public OverlayForm(Screen screen)
        {
            InitializeComponent();
            SetupOverlay(screen);

            imageChangeTimer = new System.Windows.Forms.Timer();
            imageChangeTimer.Interval = 60000;
            imageChangeTimer.Tick += (s, e) => LoadRandomImage();
            imageChangeTimer.Start();

            // Load first image after a short delay
            Task.Delay(1000).ContinueWith(_ => LoadRandomImage());
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // Set all necessary styles during window creation
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        private void MaintainWindowStyles()
        {
            try
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    // Continuously reapply the window styles to prevent them from being lost
                    int currentStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
                    int requiredStyle = currentStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;

                    if (currentStyle != requiredStyle)
                    {
                        SetWindowLong(this.Handle, GWL_EXSTYLE, requiredStyle);
                    }

                    // Ensure it stays topmost
                    SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                               SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
            catch { }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.imagePictureBox = new PictureBox();
            this.imagePictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            this.imagePictureBox.BackColor = Color.Transparent;
            this.imagePictureBox.Anchor = AnchorStyles.None;

            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Black;
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Opacity = 0.1; // Apply transparency to entire form instead of darkening image

            this.Controls.Add(this.imagePictureBox);
            this.ResumeLayout(false);
        }

        private void SetupOverlay(Screen screen)
        {
            this.Bounds = screen.Bounds;
            this.Location = screen.Bounds.Location;
            this.Size = screen.Bounds.Size;

            // Make PictureBox fill entire screen
            this.imagePictureBox.Size = screen.Bounds.Size;
            this.imagePictureBox.Location = new Point(0, 0);
            this.imagePictureBox.SizeMode = PictureBoxSizeMode.Normal; // We handle sizing manually
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                imageChangeTimer?.Dispose();
                styleMaintenanceTimer?.Dispose();
                imagePictureBox?.Image?.Dispose();
                imagePictureBox?.Dispose();
            }
            base.Dispose(disposing);
        }

        private async void LoadRandomImage()
        {
            try
            {
                string imageUrl = await GetRandomImageUrl();
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    httpClient.DefaultRequestHeaders.Clear();
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                    using (var response = await httpClient.GetAsync(imageUrl))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            using (var stream = await response.Content.ReadAsStreamAsync())
                            {
                                var originalImage = Image.FromStream(stream);
                                var compositeImage = CreateCompositeImage(originalImage, this.imagePictureBox.Size);

                                if (this.InvokeRequired)
                                {
                                    this.Invoke(new Action(() => {
                                        this.imagePictureBox.Image?.Dispose();
                                        this.imagePictureBox.Image = compositeImage;
                                    }));
                                }
                                else
                                {
                                    this.imagePictureBox.Image?.Dispose();
                                    this.imagePictureBox.Image = compositeImage;
                                }

                                originalImage.Dispose();
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private async Task<string> GetRandomImageUrl()
        {
            try
            {
                // Much larger page range - up to 500 pages instead of 50
                int randomPage = random.Next(0, 500);

                // Get up to 100 images per request (max for most APIs)
                string apiUrl = $"https://yande.re/post.json?tags=feet&limit=100&page={randomPage}";

                string jsonResponse = await httpClient.GetStringAsync(apiUrl);

                if (!string.IsNullOrEmpty(jsonResponse) && jsonResponse.Trim().StartsWith("["))
                {
                    var posts = System.Text.Json.JsonDocument.Parse(jsonResponse);
                    int postCount = posts.RootElement.GetArrayLength();

                    if (postCount > 0)
                    {
                        // Randomly select one image from the batch instead of always taking the first
                        int randomIndex = random.Next(0, postCount);
                        var randomPost = posts.RootElement[randomIndex];

                        if (randomPost.TryGetProperty("file_url", out var fileUrlElement))
                        {
                            string fileUrl = fileUrlElement.GetString();
                            return fileUrl?.StartsWith("//") == true ? "https:" + fileUrl : fileUrl;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private Image CreateCompositeImage(Image original, Size screenSize)
        {
            var composite = new Bitmap(screenSize.Width, screenSize.Height);

            using (var graphics = Graphics.FromImage(composite))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                // === CREATE BLURRED BACKGROUND ===
                // Scale image to fill screen completely (may crop)
                float bgScaleX = (float)screenSize.Width / original.Width;
                float bgScaleY = (float)screenSize.Height / original.Height;
                float bgScale = Math.Max(bgScaleX, bgScaleY); // Fill screen completely

                int bgWidth = (int)(original.Width * bgScale);
                int bgHeight = (int)(original.Height * bgScale);

                // Create blurred version
                var blurredBg = CreateBlurredImage(original, bgWidth, bgHeight);

                // Center the background
                int bgX = (screenSize.Width - bgWidth) / 2;
                int bgY = (screenSize.Height - bgHeight) / 2;

                graphics.DrawImage(blurredBg, bgX, bgY);
                blurredBg.Dispose();

                // === CREATE FITTED MAIN IMAGE ===
                // Scale image to fit within screen (no cropping)
                float mainScaleX = (float)screenSize.Width / original.Width;
                float mainScaleY = (float)screenSize.Height / original.Height;
                float mainScale = Math.Min(mainScaleX, mainScaleY); // Fit within screen

                int mainWidth = (int)(original.Width * mainScale);
                int mainHeight = (int)(original.Height * mainScale);

                // Center the main image
                int mainX = (screenSize.Width - mainWidth) / 2;
                int mainY = (screenSize.Height - mainHeight) / 2;

                var mainRect = new Rectangle(mainX, mainY, mainWidth, mainHeight);
                graphics.DrawImage(original, mainRect);
            }

            return composite;
        }

        private Image CreateBlurredImage(Image original, int width, int height)
        {
            // First resize the image to a smaller size for faster processing
            int blurWidth = width / 4; // Quarter size for blur processing
            int blurHeight = height / 4;

            var smallResized = new Bitmap(blurWidth, blurHeight);
            using (var g = Graphics.FromImage(smallResized))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(original, 0, 0, blurWidth, blurHeight);
            }

            // Apply multiple blur passes for better effect
            var blurred = smallResized;
            for (int pass = 0; pass < 3; pass++)
            {
                var nextBlur = ApplyFastBlur(blurred, 2);
                if (blurred != smallResized) blurred.Dispose();
                blurred = nextBlur;
            }

            // Scale back up to full size
            var finalBlurred = new Bitmap(width, height);
            using (var g = Graphics.FromImage(finalBlurred))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                // Apply opacity while drawing
                var colorMatrix = new System.Drawing.Imaging.ColorMatrix();
                colorMatrix.Matrix33 = 0.4f; // 40% opacity for background

                var attributes = new System.Drawing.Imaging.ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(blurred, new Rectangle(0, 0, width, height),
                           0, 0, blurred.Width, blurred.Height, GraphicsUnit.Pixel, attributes);
            }

            blurred.Dispose();
            if (smallResized != blurred) smallResized.Dispose();
            return finalBlurred;
        }

        private Bitmap ApplyFastBlur(Bitmap source, int radius)
        {
            var result = new Bitmap(source.Width, source.Height);

            // Simple box blur with sampling
            for (int y = 0; y < source.Height; y += 2) // Sample every other pixel for speed
            {
                for (int x = 0; x < source.Width; x += 2)
                {
                    int totalR = 0, totalG = 0, totalB = 0, count = 0;

                    // Sample fewer points in a cross pattern
                    for (int dy = -radius; dy <= radius; dy += radius)
                    {
                        for (int dx = -radius; dx <= radius; dx += radius)
                        {
                            int nx = Math.Max(0, Math.Min(source.Width - 1, x + dx));
                            int ny = Math.Max(0, Math.Min(source.Height - 1, y + dy));

                            Color pixel = source.GetPixel(nx, ny);
                            totalR += pixel.R;
                            totalG += pixel.G;
                            totalB += pixel.B;
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        Color avgColor = Color.FromArgb(totalR / count, totalG / count, totalB / count);
                        // Fill a 2x2 block
                        result.SetPixel(x, y, avgColor);
                        if (x + 1 < result.Width) result.SetPixel(x + 1, y, avgColor);
                        if (y + 1 < result.Height) result.SetPixel(x, y + 1, avgColor);
                        if (x + 1 < result.Width && y + 1 < result.Height) result.SetPixel(x + 1, y + 1, avgColor);
                    }
                }
            }

            return result;
        }

        protected override bool ShowWithoutActivation => true;
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            foreach (Screen screen in Screen.AllScreens)
            {
                var overlay = new OverlayForm(screen);
                overlay.Show();
            }

            Application.Run();
        }
    }
}