using System;

namespace Chummer.Controls.Shared
{
    partial class BindingListDisplay<TType>
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                try
                {
                    Contents.ListChanged -= ContentsChanged;
                }
                catch (ObjectDisposedException)
                {
                    //swallow this
                }
                System.Windows.Forms.Application.Idle -= ApplicationOnIdle;
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.pnlDisplay = new System.Windows.Forms.Panel();
            this.SuspendLayout();
            // 
            // pnlDisplay
            // 
            this.pnlDisplay.Location = new System.Drawing.Point(0, 0);
            this.pnlDisplay.Name = "pnlDisplay";
            this.pnlDisplay.Size = new System.Drawing.Size(637, 477);
            this.pnlDisplay.TabIndex = 0;
            // 
            // BindingListDisplay
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Inherit;
            this.AutoScroll = true;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.Controls.Add(this.pnlDisplay);
            this.DoubleBuffered = true;
            this.Name = "BindingListDisplay";
            this.Size = new System.Drawing.Size(640, 480);
            this.Load += new System.EventHandler(this.BindingListDisplay_Load);
            this.Scroll += new System.Windows.Forms.ScrollEventHandler(this.BindingListDisplay_Scroll);
            this.SizeChanged += new System.EventHandler(this.BindingListDisplay_SizeChanged);
            this.DpiChangedAfterParent += new System.EventHandler(this.BindingListDisplay_DpiChangedAfterParent);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel pnlDisplay;
    }
}
