// VncSharp - .NET VNC Client Library
// Copyright (C) 2008 David Humphrey
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA

using System;
using System.Windows.Forms;
using System.Drawing;
using log4net;

namespace VncSharp
{
	/// <summary>
	/// A scaledToFitScreen version of VncDesktopTransformPolicy.
	/// </summary>
	public sealed class VncScaledDesktopPolicy : VncDesktopTransformPolicy
	{
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public VncScaledDesktopPolicy(VncClient vnc, RemoteDesktop remoteDesktop) 
            : base(vnc, remoteDesktop)
        {
        }

        public override Size AutoScrollMinSize {
            get {
                return new Size(100, 100);
            }
        }

        public override Rectangle AdjustUpdateRectangle(Rectangle updateRectangle)
        {
            Size scaledToFitScreenSize = GetScaledSize(remoteDesktop.ClientRectangle.Size);
            Rectangle adjusted = new Rectangle(AdjusteNormalToScaled(updateRectangle.X) + ((remoteDesktop.ClientRectangle.Width - scaledToFitScreenSize.Width) / 2),
                                               AdjusteNormalToScaled(updateRectangle.Y) + ((remoteDesktop.ClientRectangle.Height - scaledToFitScreenSize.Height) / 2),
                                               AdjusteNormalToScaled(updateRectangle.Width),
                                               AdjusteNormalToScaled(updateRectangle.Height));
			adjusted.Inflate(1, 1);
            return adjusted;
        }
        public override Rectangle RepositionImage(Image desktopImage)
        {
            /*
            if (log.IsDebugEnabled) log.Debug("VncScaledDesktopPolicy.RepositionImage() remoteDesktop.ClientRectangle X="
                + remoteDesktop.ClientRectangle.X + " Y=" + remoteDesktop.ClientRectangle.Y 
                + " Width=" + remoteDesktop.ClientRectangle.Width + " Height=" + remoteDesktop.ClientRectangle.Height);
                */
            return GetScaledRectangle(remoteDesktop.ClientRectangle);
        }

        public override Point UpdateRemotePointer(Point current)
        {
            return GetScaledMouse(current);
        }

        public override Rectangle GetMouseMoveRectangle()
        {
            return GetScaledRectangle(remoteDesktop.ClientRectangle);
        }

        public override Point GetMouseMovePoint(Point current)
        {
            return GetScaledMouse(current);
        }

        private Size GetScaledSize(Size s)
		{
            if (vnc == null)
                return new Size(remoteDesktop.Width, remoteDesktop.Height);
            if (vnc.Framebuffer == null)
                return new Size(remoteDesktop.Width, remoteDesktop.Height);

            if (((double)s.Width / vnc.Framebuffer.Width) <= ((double)s.Height / vnc.Framebuffer.Height))
            {
                /*
                if (log.IsDebugEnabled) log.Debug("GetScaledSizeA Size Width=" + s.Width + " Height=" + s.Height
                    + " vnc.Framebuffer Width=" + vnc.Framebuffer.Width + " Heigth=" + vnc.Framebuffer.Height);
                    */
                return new Size(s.Width, (int)((double)s.Width / vnc.Framebuffer.Width * vnc.Framebuffer.Height));
			}
            else
            {
                /*
                if (log.IsDebugEnabled) log.Debug("GetScaledSizeB Size Width=" + s.Width + " Height=" + s.Height
                    + " vnc.Framebuffer Width=" + vnc.Framebuffer.Width + " Heigth=" + vnc.Framebuffer.Height);
                    */
                return new Size((int)((double)s.Height / vnc.Framebuffer.Height * vnc.Framebuffer.Width), s.Height);
			}
		}

        private double ScaleFactor {
			get {
				if (((double)remoteDesktop.ClientRectangle.Width / vnc.Framebuffer.Width) <= 
                    ((double)remoteDesktop.ClientRectangle.Height / vnc.Framebuffer.Height)) {
					return ((double)remoteDesktop.ClientRectangle.Width / vnc.Framebuffer.Width);
				} else {
					return ((double)remoteDesktop.ClientRectangle.Height / vnc.Framebuffer.Height);
				}
			}
		}

        private Point GetScaledMouse(Point src)
		{
            Size scaledToFitScreenSize = GetScaledSize(remoteDesktop.ClientRectangle.Size);
			src.X = AdjusteScaledToNormal(src.X - ((remoteDesktop.ClientRectangle.Width - scaledToFitScreenSize.Width) / 2));
			src.Y = AdjusteScaledToNormal(src.Y - ((remoteDesktop.ClientRectangle.Height - scaledToFitScreenSize.Height) / 2));
            return src;
        }

		private Rectangle GetScaledRectangle(Rectangle rect)
		{
			Size scaledToFitScreenSize = GetScaledSize(rect.Size);
            /*
            if (log.IsDebugEnabled) log.Debug("GetScaledRectangle() "
                + " rect Width=" + rect.Width + " Height=" + rect.Height
                + " scaledToFitScreenSize Width=" + scaledToFitScreenSize.Width + "Height=" + scaledToFitScreenSize.Height
                );
                */
			return new Rectangle((rect.Width - scaledToFitScreenSize.Width) / 2,
                                 (rect.Height - scaledToFitScreenSize.Height) / 2, 
                                 scaledToFitScreenSize.Width, 
                                 scaledToFitScreenSize.Height);
		}

        private int AdjusteScaledToNormal(double value)
		{
			return (int)Math.Round(value / ScaleFactor);
		}

		private int AdjusteNormalToScaled(double value)
		{
			return (int)Math.Round(value * ScaleFactor);
 		}  
    }
}