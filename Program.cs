using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace Dumper2ch;

class Program
{
	static DownloaderUI UI;

	static readonly string[] MediaFileExtensions = "mp4;webm;jpeg;jpg;png;gif;bmp;webp".Split(";");

	static readonly Regex ThreadUriRegex =
		new Regex(@"2ch[.]hk/(.*?)/res/([0-9]*)[.]html");
	static readonly Regex MediaRegex =
		new Regex($"(data-src|src|href)=\"/(.*?)/([0-9]*)[.]({string.Join('|', MediaFileExtensions)})\"", RegexOptions.IgnorePatternWhitespace);

	static readonly DirectoryInfo SaveDirectoryInfo =
		new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.Personal) + @"\Dumper2ch");

	const string Trc = @"Trace>>";
	const string Wrn = @"Warning>>";
	const string Err = @"Error>>";

	[DllImport("kernel32")] static extern bool AllocConsole();
	[STAThread]
	public static void Main()
	{
		AllocConsole();
		Trace.Listeners.Add(new ConsoleTraceListener());
		Trace.WriteLine(Trc + "Console initialized and listening Trace");

		Trace.WriteLine(Trc + "Ensuring application folder is created in user personal");
		SaveDirectoryInfo.Create();

		Trace.WriteLine(Trc + "Creating UI instance");
		UI = new DownloaderUI();

		Trace.WriteLine(Trc + "Binding download button");
		UI.DownloadButtonClick += DownloadButton_Click;

		Trace.WriteLine(Trc + "Running UI");
		Application.Run((Form)UI);

	}


	public static void DownloadButton_Click(object sender, EventArgs ea)
	{
		Trace.WriteLine(Trc + "Download button clicked");

		Trace.WriteLine(Trc + "Disabling UI");
		((Form)(UI)).Enabled = false;

		Trace.WriteLine(Trc + "Initializing WebClient with given usercode_auth cookie");
		WebClient DownloaderWC = new WebClient();
		if (!string.IsNullOrEmpty(UI.UserCodeAuth.Trim())) DownloaderWC.Headers.Add(HttpRequestHeader.Cookie, $"usercode_auth={UI.UserCodeAuth}");
		else Trace.WriteLine(Wrn + "Unacceptable usercode_auth parameter was ignored");

		HtmlDocument CurrentPage = new HtmlDocument();
		IEnumerable<Uri> MediaUris;
		DirectoryInfo CurrentDirectory;

		Trace.WriteLine(Trc + "Beginning recognizing links");

		foreach (Uri ThreadUriMatch in GetThreadURIsFromText(UI.UrisInput))
		{
			Trace.WriteLine(Trc + $"Thread url \"{ThreadUriMatch}\" recognized and pending download");

			try
			{
				CurrentPage.LoadHtml(DownloaderWC.DownloadString(ThreadUriMatch));
			}
			catch (Exception e)
			{
				Trace.WriteLine(Err + $"Exception while trying to access thread page at \"{ThreadUriMatch}\": \"{e.Message}\"");
				continue;
			}

			Trace.WriteLine(Trc + $"Thread page at \"{ThreadUriMatch}\" downloaded and pending parsing");

			try
			{
				MediaUris = GetMediaURIsFromThreadHtmlPage(CurrentPage);
			}
			catch (Exception e)
			{
				Trace.WriteLine(Err + $"Exception while parsing page at \"{ThreadUriMatch}\" to media links: \"{e.Message}\"");
				continue;
			}

			Trace.WriteLine(Trc + $"Total media links found at \"{ThreadUriMatch}\": {MediaUris.Count()}.");
			Trace.WriteLine(Trc + $"Media links found at \"{ThreadUriMatch}\": \"{string.Join(';', MediaUris)}\"");

			Trace.WriteLine(Trc + $"Beginning download files from thread at \"{ThreadUriMatch}\".");

			if (UI.SubFoldersRequired)
			{
				Trace.WriteLine(Trc + $"Subdirectory is required and created for thread at \"{ThreadUriMatch}\" and pending creating");

				string threadName = null;
				try {
					threadName = CurrentPage.DocumentNode.SelectSingleNode("//span[contains(@class, 'post__title')]").InnerHtml.Trim();
					if (string.IsNullOrWhiteSpace(threadName) || threadName.Length > byte.MaxValue)
                       throw new Exception();
                }
				catch
				{
					Trace.WriteLine(Wrn + "Thread name is empty or too long for subdirectory name, using thread number instead.");
                    threadName = GetThreadNumberFromURI(ThreadUriMatch);
                }
				
				CurrentDirectory = SaveDirectoryInfo.CreateSubdirectory(threadName);
			}
			else
			{
				Trace.WriteLine(Trc + $"Subdirectory is not required for thread at \"{ThreadUriMatch}\"");
				CurrentDirectory = SaveDirectoryInfo;
			}
			Trace.WriteLine(Trc + $"Directory at {CurrentDirectory.FullName} will be used for current thread medias");

			foreach (Uri MediaUri in MediaUris)
			{
				FileInfo CurrentFile = new FileInfo(CurrentDirectory.FullName + '\\' + GetFileNameFromURI(MediaUri));

				Trace.WriteLine(Trc + $"Pending download file at \"{MediaUri}\" from thread at \"{ThreadUriMatch}\" as \"{CurrentFile.FullName}\"");

				if (CurrentFile.Exists && CurrentFile.Length > 1)
				{
					Trace.WriteLine(Trc + $"File at \"{MediaUri}\" from thread at \"{ThreadUriMatch}\" skipped as existing at \"{CurrentFile.FullName}\".");
					continue;
				}

				try
				{
					DownloaderWC.DownloadFile(MediaUri, CurrentFile.FullName);
				}
				catch (Exception e)
				{
					Trace.WriteLine(Err + $"Exception while trying to download file at \"{MediaUri}\" from thread at \"{ThreadUriMatch}\" as \"{CurrentFile.FullName}\": \"{e.Message}\"");
				}
				Trace.WriteLine($"File at \"{MediaUri}\" from thread at \"{ThreadUriMatch}\" saved as \"{CurrentFile.FullName}\"");
			}
		}

		Trace.WriteLine("Download process finished.");
		Trace.WriteLine("Enabling UI...");
		((Form)(UI)).Enabled = true;
	}


	static IEnumerable<Uri> GetMediaURIsFromThreadHtmlPage(HtmlDocument ThreadPage)
	{
		return
			MediaRegex.Matches(string.Concat(ThreadPage.DocumentNode.SelectNodes("//a[contains(@class, 'post__image-link')]").Select(x => x.OuterHtml.Split('>')[0])))
				.Select(x => new Uri(@"https://2ch.hk" + x.Value.Split('"')[1]));
	}
	static IEnumerable<Uri> GetThreadURIsFromText(string Text)
	{
		return
			ThreadUriRegex.Matches(Text).Select(x => new Uri(@"https://" + x.Value));
	}
	static string GetThreadNumberFromURI(Uri ThreadUri) => ThreadUri.AbsoluteUri.Split('/').Last().Split('.').First();
	static string GetFileNameFromURI(Uri FileUri) => FileUri.AbsoluteUri.Split('/').Last();
}


class DownloaderUI
{
	Label UsercodeAuthLabel = new Label { AutoSize = true, Text = "usercode_auth", Width = 100 };
	TextBox UsercodeAuthTextBox = new TextBox { Text = @"" };
	Label InfoLabel = new Label { Text = "Inset your 2ch.hk thread URIs here:" };
	RichTextBox URIsTextBox = new RichTextBox { };
	CheckBox CreateSubFoldersCheckBox = new CheckBox { Text = "Create sub-folder for each thread", Checked = true };
	Button DownloadButton = new Button { Text = "Start downloading" };
	Form MainForm = new Form { };

	public DownloaderUI()
	{
		Control[] AllControls = { Controls.ToHorizontalStackPanel(new Control[] { UsercodeAuthLabel, UsercodeAuthTextBox }), InfoLabel, URIsTextBox, CreateSubFoldersCheckBox, DownloadButton };
		Controls.ToSameWidth(AllControls, 300);
		Panel AllElementsPanel = Controls.ToVerticalStackPanel(AllControls);
		MainForm.Controls.Add(AllElementsPanel);
		MainForm.ClientSize = new Size(AllElementsPanel.Width, AllElementsPanel.Height);
		MainForm.MinimumSize = MainForm.MaximumSize = MainForm.Size;
		UsercodeAuthTextBox.Width = AllElementsPanel.ClientSize.Width - UsercodeAuthLabel.Width;
	}

	public static explicit operator Form(DownloaderUI dui)
	{
		return dui.MainForm;
	}

	public Form Form => MainForm;
	public string UrisInput => URIsTextBox.Text;
	public bool SubFoldersRequired => CreateSubFoldersCheckBox.Checked;
	public string UserCodeAuth => UsercodeAuthTextBox.Text;

	public event EventHandler DownloadButtonClick
	{
		add => DownloadButton.Click += value;
		remove => DownloadButton.Click -= value;
	}
}

class Controls
{
	public static Panel ToVerticalStackPanel(IList<Control> Controls)
	{
		Panel Out = new Panel();
		int CurrentHeight = 0;

		for (int i = 0; i < Controls.Count; i++)
		{
			Controls[i].Location = new Point(0, CurrentHeight);
			Out.Controls.Add(Controls[i]);
			CurrentHeight += Controls[i].Height;
		}

		Out.Height = Controls.Last().Location.Y + Controls.Last().Height;
		Out.Width = Controls.Max(x => x.Width);
		Out.BackColor = Color.Transparent;

		return Out;
	}
	public static Panel ToHorizontalStackPanel(IList<Control> Controls)
	{
		Panel Out = new Panel();
		int CurrentWidth = 0;

		for (int i = 0; i < Controls.Count; i++)
		{
			Controls[i].Location = new Point(CurrentWidth, 0);
			Out.Controls.Add(Controls[i]);
			CurrentWidth += Controls[i].Width;
		}

		Out.Width = CurrentWidth;
		Out.Height = Controls.Max(x => x.Height);
		Out.BackColor = Color.Transparent;

		return Out;
	}


	public static void ToSameWidth(IList<Control> Controls, int Width)
	{
		for (int i = 0; i < Controls.Count; i++) Controls[i].Width = Width;
	}
}