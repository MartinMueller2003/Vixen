﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Common.Controls.Scaling;
using Common.Controls.Theme;
using Common.Resources;
using Common.Resources.Properties;

namespace VixenModules.Preview.VixenPreview.Shapes
{
	public partial class PreviewLineSetupControl : DisplayItemBaseControl
	{
		public PreviewLineSetupControl(PreviewBaseShape shape) : base(shape)
		{
			InitializeComponent();
			ThemeUpdateControls.UpdateControls(this);
			ThemePropertyGridRenderer.PropertyGridRender(propertyGrid);
			int iconSize = (int)(16 * ScalingTools.GetScaleFactor());
			buttonHelp.Image = Tools.GetIcon(Resources.help, iconSize);
			propertyGrid.SelectedObject = Shape;
			Shape.OnPropertiesChanged += OnPropertiesChanged;
		}

		~PreviewLineSetupControl()
		{
			Shape.OnPropertiesChanged -= OnPropertiesChanged;
		}

		private void OnPropertiesChanged(object sender, PreviewBaseShape shape)
		{
			propertyGrid.Refresh();
		}

		private void buttonHelp_Click(object sender, EventArgs e)
		{
			Common.VixenHelp.VixenHelp.ShowHelp(Common.VixenHelp.VixenHelp.HelpStrings.Preview_BasicShapes);
		}

		private void buttonBackground_MouseHover(object sender, EventArgs e)
		{
			var btn = (Button)sender;
			btn.BackgroundImage = Resources.ButtonBackgroundImageHover;
		}

		private void buttonBackground_MouseLeave(object sender, EventArgs e)
		{
			var btn = (Button)sender;
			btn.BackgroundImage = Resources.ButtonBackgroundImage;
		}
	}
}