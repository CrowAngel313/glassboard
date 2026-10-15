using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GlassBoard;

public class Controller
{
	private WidgetManager widgets;

	public Preferences Prefs;

	public DockWindow Dock;

	public BoardWindow Board;

	public NotifyIcon Tray;

	private bool importing;

	public bool ShuttingDown;

	private bool settingsOpen;

	private Window settingsWindow;

	private System.Windows.Controls.TabControl settingsTabs;

	private readonly BoardEditHistory editHistory = new BoardEditHistory();

	public bool UiBusy
	{
		get
		{
			if (!settingsOpen)
			{
				return Board.ModalBusy;
			}
			return true;
		}
	}

	private Window OwnerWindow
	{
		get
		{
			if (!Board.IsVisible)
			{
				return Dock;
			}
			return Board;
		}
	}

	public string WidgetStatus
	{
		get
		{
			if (widgets != null)
			{
				return widgets.LastError;
			}
			return "";
		}
	}

	public bool CanUndoRemoval => editHistory.CanUndo;

	public bool CanRedoBoard => editHistory.CanRedo;

	public string UndoLabel => editHistory.UndoLabel;

	public void RefreshWidgets()
	{
		if (widgets == null)
		{
			widgets = new WidgetManager(this);
		}
		widgets.Sync();
	}

	public void SetWidgetEnabled(string kind, bool value)
	{
		if (widgets == null)
		{
			widgets = new WidgetManager(this);
		}
		widgets.Enable(kind, value);
	}

	public void RecoverWidgets()
	{
		try
		{
			if (File.Exists(Store.FileName))
			{
				File.Copy(Store.FileName, Store.FileName + ".before-widget-recovery-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), overwrite: false);
			}
			WidgetRules.Recover(Prefs.Widgets);
			Save();
			Store.Trace("RECOVER enable all widgets, preserve contents and layout");
			ReopenWidgets();
		}
		catch (Exception ex)
		{
			Store.Log(ex);
			System.Windows.MessageBox.Show(ex.Message, "组件恢复失败");
		}
	}

	public void ReopenWidgets()
	{
		if (widgets == null)
		{
			widgets = new WidgetManager(this);
		}
		widgets.Reopen();
		if (!string.IsNullOrEmpty(widgets.LastError))
		{
			System.Windows.MessageBox.Show(widgets.LastError + "\n详细记录位于配置文件夹 error.log。", "组件打开失败");
		}
	}

	public void CloseWidgets()
	{
		if (widgets != null)
		{
			widgets.Dispose();
			widgets = null;
		}
	}

	public void ConfigureWidgets()
	{
		Settings(3);
	}

	public void AddFolder()
	{
		bool modalBusy = Board.ModalBusy;
		Board.ModalBusy = true;
		try
		{
			string text = FeatureUi.PickFolder(OwnerWindow, "选择要放入玻璃板的文件夹（添加文件夹本身）");
			if (text != null)
			{
				Add(new string[1] { text });
				Board.ClearSearch();
				Board.SetStatus("已添加文件夹入口");
			}
		}
		finally
		{
			Board.ModalBusy = modalBusy;
		}
	}

	public void ShowNotes()
	{
		Prefs.Widgets.Notes = true;
		RefreshWidgets();
		Save();
	}

	private VisualizerWindow visualizer;

	/// <summary>音频律动小组件开关。音频初始化失败时提示，不影响主程序。</summary>
	public void ToggleVisualizer()
	{
		try
		{
			if (visualizer != null)
			{
				try { visualizer.Close(); } catch { }
				visualizer = null;
				return;
			}
			if (!AudioMeter.Init())
			{
				System.Windows.MessageBox.Show(OwnerWindow, "未检测到音频输出设备，律动小组件不可用。", "GlassBoard");
				return;
			}
			visualizer = new VisualizerWindow();
			visualizer.Closed += delegate { visualizer = null; };
			visualizer.Show();
		}
		catch (Exception ex)
		{
			Store.Log(ex);
		}
	}

	public async void ToggleTaskbarAutoHide(bool? enabled = null)
	{
		try
		{
			bool flag = enabled ?? (!TaskbarAutoHide.Enabled);
			if (flag)
			{
				DesktopVisibility.SetHidden(taskbar: true, hide: false);
			}
			TaskbarAutoHide.Set(flag);
			Board.SetStatus("已请求" + (flag ? "启用" : "关闭") + " Windows 任务栏自动隐藏");
			await Task.Delay(250);
			if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
			{
				Dock.Refresh();
			}
		}
		catch (Exception ex)
		{
			Store.Log(ex);
			System.Windows.MessageBox.Show(ex.Message, "Windows 任务栏自动隐藏");
		}
	}

	public void Customize(Entry entry)
	{
		bool modalBusy = Board.ModalBusy;
		Board.ModalBusy = true;
		try
		{
			Window w = FeatureUi.Dialog("名称与图标", OwnerWindow, 500.0, 280.0);
			StackPanel stackPanel = new StackPanel
			{
				Margin = new Thickness(22.0)
			};
			w.Content = stackPanel;
			stackPanel.Children.Add(new TextBlock
			{
				Text = "显示名称",
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			});
			System.Windows.Controls.TextBox name = new System.Windows.Controls.TextBox
			{
				Text = entry.Name,
				MinHeight = 38.0
			};
			stackPanel.Children.Add(name);
			string icon = entry.IconPath;
			TextBlock info = new TextBlock
			{
				Text = (string.IsNullOrWhiteSpace(icon) ? "使用软件原图标" : "已使用自定义图标"),
				Margin = new Thickness(0.0, 12.0, 0.0, 12.0)
			};
			stackPanel.Children.Add(info);
			WrapPanel wrapPanel = new WrapPanel();
			stackPanel.Children.Add(wrapPanel);
			wrapPanel.Children.Add(FeatureUi.Button("选择图标", delegate
			{
				Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
				{
					Filter = "图标图片|*.png;*.ico;*.jpg;*.jpeg;*.bmp"
				};
				if (openFileDialog.ShowDialog(w).GetValueOrDefault())
				{
					try
					{
						icon = FeatureUi.StoreIcon(openFileDialog.FileName);
						info.Text = "已选择图标，点击保存应用";
					}
					catch (Exception ex)
					{
						System.Windows.MessageBox.Show(w, ex.Message, "无法读取图标");
					}
				}
			}));
			wrapPanel.Children.Add(FeatureUi.Button("恢复原图标", delegate
			{
				icon = null;
				info.Text = "使用软件原图标";
			}));
			stackPanel.Children.Add(FeatureUi.Button("保存", delegate
			{
				if (!string.IsNullOrWhiteSpace(name.Text))
				{
					BoardEditState before = BeginBoardEdit();
					entry.Name = name.Text.Trim();
					entry.IconPath = icon;
					EndBoardEdit(before, "自定义名称与图标");
					Refresh();
					w.Close();
				}
			}));
			w.ShowDialog();
		}
		finally
		{
			Board.ModalBusy = modalBusy;
		}
	}

	public void AddExistingToGroup(string group, Window owner = null)
	{
		if (group == "全部")
		{
			return;
		}
		bool modalBusy = Board.ModalBusy;
		Board.ModalBusy = true;
		try
		{
			Window w = FeatureUi.Dialog("添加已有图标到「" + group + "」", owner ?? Board, 680.0, 620.0);
			DockPanel dockPanel = new DockPanel
			{
				Margin = new Thickness(22.0)
			};
			w.Content = dockPanel;
			StackPanel stackPanel = new StackPanel();
			DockPanel.SetDock(stackPanel, System.Windows.Controls.Dock.Top);
			dockPanel.Children.Add(stackPanel);
			stackPanel.Children.Add(new TextBlock
			{
				Text = "同一软件可以出现在多个分组",
				FontSize = 18.0,
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			});
			stackPanel.Children.Add(new TextBlock
			{
				Text = ((group == "未分组") ? "选择后会移回未分组。" : "添加后保留原有分组；全部视图只显示一次。"),
				Foreground = System.Windows.Media.Brushes.LightGray,
				Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
			});
			System.Windows.Controls.TextBox query = new System.Windows.Controls.TextBox
			{
				MinHeight = 38.0,
				ToolTip = "搜索已有软件",
				Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
			};
			stackPanel.Children.Add(query);
			WrapPanel wrapPanel = new WrapPanel();
			DockPanel.SetDock(wrapPanel, System.Windows.Controls.Dock.Bottom);
			dockPanel.Children.Add(wrapPanel);
			System.Windows.Controls.ListBox list = new System.Windows.Controls.ListBox
			{
				Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
			};
			dockPanel.Children.Add(list);
			List<Entry> candidates = Prefs.Apps.Where((Entry a) => !FeatureRules.InGroup(a, group)).ToList();
			HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			Action fill = delegate
			{
				list.Items.Clear();
				foreach (Entry item in candidates.Where((Entry a) => LayoutRules.Matches(a.Name, query.Text.Trim())))
				{
					Entry entry = item;
					System.Windows.Controls.CheckBox checkBox = new System.Windows.Controls.CheckBox
					{
						Content = entry.Name + "  ·  " + string.Join(" / ", FeatureRules.Memberships(entry)),
						IsChecked = selected.Contains(entry.Path),
						Margin = new Thickness(7.0),
						ToolTip = entry.Path
					};
					checkBox.Checked += delegate
					{
						selected.Add(entry.Path);
					};
					checkBox.Unchecked += delegate
					{
						selected.Remove(entry.Path);
					};
					list.Items.Add(checkBox);
				}
			};
			query.TextChanged += delegate
			{
				fill();
			};
			wrapPanel.Children.Add(FeatureUi.Button("全选结果", delegate
			{
				foreach (Entry item2 in candidates.Where((Entry a) => LayoutRules.Matches(a.Name, query.Text.Trim())))
				{
					selected.Add(item2.Path);
				}
				fill();
			}));
			wrapPanel.Children.Add(FeatureUi.Button("取消全选", delegate
			{
				selected.Clear();
				fill();
			}));
			wrapPanel.Children.Add(FeatureUi.Button("添加到本组", delegate
			{
				BoardEditState before = BeginBoardEdit();
				int num = FeatureRules.AddShared(Prefs, selected, group);
				EndBoardEdit(before, "添加已有图标");
				Refresh();
				Board.ClearSearch();
				Board.SetStatus("已添加 " + num + " 个已有图标到「" + group + "」");
				w.Close();
			}));
			wrapPanel.Children.Add(FeatureUi.Button("关闭", delegate
			{
				w.Close();
			}));
			fill();
			w.ShowDialog();
		}
		finally
		{
			Board.ModalBusy = modalBusy;
		}
	}

	public void AutoGroupApps(Window owner = null)
	{
		BoardEditState before = BeginBoardEdit();
		AutoGroupRules.ReloadCatalog();
		foreach (Entry item in Prefs.Apps.Where((Entry a) => FeatureRules.GroupName(a) == "未分组"))
		{
			LaunchInfoService.Enrich(item);
		}
		int num = AutoGroupRules.ApplyUnassigned(Prefs);
		EndBoardEdit(before, "自动分组");
		Refresh();
		int num2 = Prefs.Apps.Count((Entry a) => FeatureRules.GroupName(a) == "未分组");
		System.Windows.MessageBox.Show(owner ?? OwnerWindow, "已自动分组 " + num + " 个软件。\n还有 " + num2 + " 个未识别，可手动移动到分组。已有手动分组已保留。", "GlassBoard");
	}

	/// <summary>
	/// 智能去重：同一目录下名称首词相同的自动扫描条目只保留一个"主要的"
	/// （如 Git Bash / Git CMD / Git GUI 只留一个，Cheat Engine 多个版本只留一个）。
	/// 保留规则：名称与所在目录同名优先，否则名称最短者。手动添加的不参与合并。
	/// 返回被合并掉的数量。
	/// </summary>
	public int MergeSimilarEntries()
	{
		Dictionary<string, List<Entry>> groups = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
		foreach (Entry a in Prefs.Apps)
		{
			string key = StartupEntryRules.SmartMergeKey(a);
			if (key == null)
			{
				continue;
			}
			if (!groups.TryGetValue(key, out List<Entry> list))
			{
				list = new List<Entry>();
				groups[key] = list;
			}
			list.Add(a);
		}
		int removed = 0;
		foreach (List<Entry> cl in groups.Values)
		{
			if (cl.Count < 2)
			{
				continue;
			}
			string dirName = "";
			try
			{
				dirName = Path.GetFileName(Path.GetDirectoryName(cl[0].LaunchTarget ?? cl[0].Path ?? "") ?? "");
			}
			catch
			{
			}
			Entry win = cl.FirstOrDefault((Entry a) => string.Equals((a.Name ?? "").Trim(), dirName, StringComparison.OrdinalIgnoreCase));
			if (win == null)
			{
				win = cl.OrderBy((Entry a) => ((a.Name ?? "").Trim().Length)).First();
			}
			foreach (Entry a in cl)
			{
				if (a == win)
				{
					continue;
				}
				if (a.Pinned)
				{
					win.Pinned = true;
				}
				Prefs.Apps.Remove(a);
				removed++;
			}
		}
		return removed;
	}

	/// <summary>
	/// 一键清理非应用入口：网页书签、批处理脚本、以及「用记事本打开某文件」这类快捷方式，
	/// 并合并指向同一程序（含参数）的重复项。
	/// </summary>
	public void CleanupNonApps(Window owner = null)
	{
		BoardEditState before = BeginBoardEdit();
		List<string> junk = new List<string>();
		foreach (Entry a in Prefs.Apps)
		{
			LaunchInfoService.Enrich(a);
			if (!StartupEntryRules.RealApplication(a) || StartupEntryRules.JunkBookmark(a))
			{
				junk.Add(a.Name);
			}
		}
		// 去重：同一目标+参数只留第一个
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> dupes = new List<string>();
		foreach (Entry a in Prefs.Apps)
		{
			if (junk.Contains(a.Name))
			{
				continue;
			}
			if (!seen.Add(StartupEntryRules.DedupKey(a)))
			{
				dupes.Add(a.Name);
			}
		}
		int total = junk.Count + dupes.Count;
		if (total == 0)
		{
			System.Windows.MessageBox.Show(owner ?? OwnerWindow, "没有发现需要清理的条目。\n当前 " + Prefs.Apps.Count + " 个入口都是可启动的程序。", "GlassBoard");
			return;
		}
		string detail = "";
		if (junk.Count > 0)
		{
			detail += "\n\n非应用入口 " + junk.Count + " 个：\n  " + string.Join("\n  ", junk.Take(12));
			if (junk.Count > 12)
			{
				detail += "\n  …还有 " + (junk.Count - 12) + " 个";
			}
		}
		if (dupes.Count > 0)
		{
			detail += "\n\n重复入口 " + dupes.Count + " 个：\n  " + string.Join("\n  ", dupes.Take(12));
			if (dupes.Count > 12)
			{
				detail += "\n  …还有 " + (dupes.Count - 12) + " 个";
			}
		}
		if (System.Windows.MessageBox.Show(owner ?? OwnerWindow, "将清理 " + total + " 个条目（保留 " + (Prefs.Apps.Count - total) + " 个）。" + detail + "\n\n可以随时按 Ctrl+Z 撤销。是否继续？", "GlassBoard · 清理非应用入口", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
		{
			return;
		}
		HashSet<string> removeNames = new HashSet<string>(junk.Concat(dupes));
		// 去重时保留每个键的第一个，所以按「键已出现过」逐个判定
		HashSet<string> keptKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<Entry> finalList = new List<Entry>();
		foreach (Entry a in Prefs.Apps)
		{
			if (junk.Contains(a.Name))
			{
				continue;
			}
			if (!keptKeys.Add(StartupEntryRules.DedupKey(a)))
			{
				continue;
			}
			finalList.Add(a);
		}
		Prefs.Apps = finalList;
		int merged = MergeSimilarEntries();
		Prefs.Groups = FeatureRules.Groups(Prefs.Groups, Prefs.Apps);
		EndBoardEdit(before, "清理非应用入口");
		Refresh();
		Board.SetStatus("已清理 " + total + " 个非应用/重复条目" + (merged > 0 ? "，智能合并 " + merged + " 个同类项" : ""));
	}

	/// <summary>直接弹窗新建分组并命名，一步到位。</summary>
	public string CreateGroup(Window owner = null)
	{
		string name = FeatureUi.Ask(owner ?? OwnerWindow, "新分组名称");
		if (name == null)
		{
			return null;
		}
		name = name.Trim();
		if (name.Length == 0)
		{
			return null;
		}
		if (name == "全部" || name == "未分组" || name.Length > 32 || Prefs.Groups.Any((string g) => g.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
		{
			System.Windows.MessageBox.Show(owner ?? OwnerWindow, "名称重复、过长或属于内置分类。", "GlassBoard");
			return null;
		}
		BoardEditState before = BeginBoardEdit();
		Prefs.Groups.Add(name);
		EndBoardEdit(before, "新增分组");
		Refresh();
		return name;
	}

	/// <summary>在面板上新建分组并立刻切换过去，方便直接拖入软件。</summary>
	public void CreateGroupAndOpen(Window owner = null)
	{
		string name = CreateGroup(owner);
		if (name != null)
		{
			Board.OpenGroupByName(name);
			Board.SetStatus("已新建分组「" + name + "」");
		}
	}

	/// <summary>把当前分组的图标批量加入另一个分组。</summary>
	public void AddCurrentGroupToAnother(string from, Window owner = null)
	{
		if (Prefs.Apps.All((Entry a) => !FeatureRules.InGroup(a, from)))
		{
			System.Windows.MessageBox.Show(owner ?? OwnerWindow, "当前分组还没有图标。", "GlassBoard");
			return;
		}
		AddExistingToGroup(from, owner);
	}

	public void ManageGroups(Window owner = null)
	{
		bool modalBusy = Board.ModalBusy;
		Board.ModalBusy = true;
		try
		{
			Window w = FeatureUi.Dialog("软件分组", owner ?? OwnerWindow, 520.0, 450.0);
			DockPanel dockPanel = new DockPanel
			{
				Margin = new Thickness(22.0)
			};
			w.Content = dockPanel;
			WrapPanel wrapPanel = new WrapPanel();
			DockPanel.SetDock(wrapPanel, System.Windows.Controls.Dock.Bottom);
			dockPanel.Children.Add(wrapPanel);
			System.Windows.Controls.ListBox list = new System.Windows.Controls.ListBox
			{
				Margin = new Thickness(0.0, 0.0, 0.0, 14.0)
			};
			dockPanel.Children.Add(list);
			Action reload = delegate
			{
				string text4 = list.SelectedItem as string;
				list.ItemsSource = null;
				list.ItemsSource = Prefs.Groups.ToList();
				if (text4 != null)
				{
					list.SelectedItem = text4;
				}
			};
			reload();
			// 双击分组名直接改名，不必先选中再点按钮。
			list.MouseDoubleClick += delegate
			{
				if (list.SelectedItem is string text2)
				{
					string text3 = FeatureUi.Ask(w, "分组改名", text2);
					if (text3 != null && text3 != text2)
					{
						try
						{
							BoardEditState before2 = BeginBoardEdit();
							FeatureRules.RenameGroup(Prefs, text2, text3);
							EndBoardEdit(before2, "重命名分组");
							Refresh();
							reload();
						}
						catch (Exception ex2)
						{
							System.Windows.MessageBox.Show(w, ex2.Message);
						}
					}
				}
			};
			wrapPanel.Children.Add(FeatureUi.Button("新建分组并命名", delegate
			{
				string text5 = CreateGroup(w);
				if (text5 != null)
				{
					reload();
					list.SelectedItem = text5;
				}
			}));
			wrapPanel.Children.Add(FeatureUi.Button("改名", delegate
			{
				if (list.SelectedItem is string text2)
				{
					string text3 = FeatureUi.Ask(w, "分组改名", text2);
					if (text3 != null)
					{
						try
						{
							if (text3.Length > 32)
							{
								throw new ArgumentException("名称最多 32 个字");
							}
							BoardEditState before2 = BeginBoardEdit();
							FeatureRules.RenameGroup(Prefs, text2, text3);
							EndBoardEdit(before2, "重命名分组");
							Refresh();
							reload();
						}
						catch (Exception ex)
						{
							System.Windows.MessageBox.Show(w, ex.Message);
						}
					}
				}
			}));
			wrapPanel.Children.Add(FeatureUi.Button("删除分组", delegate
			{
				if (list.SelectedItem is string text && System.Windows.MessageBox.Show(w, "删除分组「" + text + "」？入口保留并移到未分组。", "GlassBoard", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
				{
					BoardEditState before = BeginBoardEdit();
					FeatureRules.DeleteGroup(Prefs, text);
					EndBoardEdit(before, "删除分组");
					Refresh();
					reload();
				}
			}));
			w.ShowDialog();
		}
		finally
		{
			Board.ModalBusy = modalBusy;
		}
	}

	public void ManageWorkspaces(Window owner = null)
	{
		bool modalBusy = Board.ModalBusy;
		Board.ModalBusy = true;
		try
		{
			Window w = FeatureUi.Dialog("工作区 · 一次打开一组工具", owner ?? OwnerWindow, 780.0, 640.0);
			Grid grid = new Grid
			{
				Margin = new Thickness(22.0)
			};
			w.Content = grid;
			grid.RowDefinitions.Add(new RowDefinition
			{
				Height = GridLength.Auto
			});
			grid.RowDefinitions.Add(new RowDefinition());
			grid.RowDefinitions.Add(new RowDefinition
			{
				Height = GridLength.Auto
			});
			StackPanel stackPanel = new StackPanel();
			grid.Children.Add(stackPanel);
			stackPanel.Children.Add(new TextBlock
			{
				Text = "把应用、素材文件夹和项目文件组成一个工作区",
				FontSize = 17.0,
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			});
			System.Windows.Controls.TextBox name = new System.Windows.Controls.TextBox
			{
				Text = "创作工作区",
				MinHeight = 38.0,
				Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
			};
			stackPanel.Children.Add(name);
			Grid grid2 = new Grid();
			grid2.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(190.0)
			});
			grid2.ColumnDefinitions.Add(new ColumnDefinition());
			Grid.SetRow(grid2, 1);
			grid.Children.Add(grid2);
			System.Windows.Controls.ListBox saved = new System.Windows.Controls.ListBox
			{
				Margin = new Thickness(0.0, 0.0, 12.0, 12.0),
				DisplayMemberPath = "Name"
			};
			grid2.Children.Add(saved);
			System.Windows.Controls.ListBox choices = new System.Windows.Controls.ListBox
			{
				Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
			};
			Grid.SetColumn(choices, 1);
			grid2.Children.Add(choices);
			Dictionary<string, System.Windows.Controls.CheckBox> picked = new Dictionary<string, System.Windows.Controls.CheckBox>(StringComparer.OrdinalIgnoreCase);
			Workspace current = null;
			Action<IEnumerable<string>> fill = delegate(IEnumerable<string> paths)
			{
				HashSet<string> hashSet = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
				choices.Items.Clear();
				picked.Clear();
				foreach (Entry item in Prefs.Apps.Concat(from path in hashSet
					where !Prefs.Apps.Any((Entry a) => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
					select new Entry
					{
						Name = Path.GetFileNameWithoutExtension(path),
						Path = path
					}).ToList())
				{
					System.Windows.Controls.CheckBox checkBox = new System.Windows.Controls.CheckBox
					{
						Content = item.Name,
						ToolTip = item.Path,
						IsChecked = hashSet.Contains(item.Path),
						Margin = new Thickness(5.0)
					};
					picked[item.Path] = checkBox;
					choices.Items.Add(checkBox);
				}
			};
			Action reload = delegate
			{
				saved.ItemsSource = null;
				saved.ItemsSource = Prefs.Workspaces.ToList();
			};
			reload();
			fill(new string[0]);
			saved.SelectionChanged += delegate
			{
				current = saved.SelectedItem as Workspace;
				if (current != null)
				{
					name.Text = current.Name;
					fill(current.Paths);
				}
			};
			WrapPanel wrapPanel = new WrapPanel();
			Grid.SetRow(wrapPanel, 2);
			grid.Children.Add(wrapPanel);
			wrapPanel.Children.Add(FeatureUi.Button("新建", delegate
			{
				saved.SelectedItem = null;
				current = null;
				name.Text = "新工作区";
				fill(new string[0]);
			}));
			wrapPanel.Children.Add(FeatureUi.Button("全选", delegate
			{
				foreach (System.Windows.Controls.CheckBox value in picked.Values)
				{
					value.IsChecked = true;
				}
			}));
			wrapPanel.Children.Add(FeatureUi.Button("取消全选", delegate
			{
				foreach (System.Windows.Controls.CheckBox value2 in picked.Values)
				{
					value2.IsChecked = false;
				}
			}));
			wrapPanel.Children.Add(FeatureUi.Button("保存工作区", delegate
			{
				string title = name.Text.Trim();
				List<string> list = (from x in picked
					where x.Value.IsChecked.GetValueOrDefault()
					select x.Key).ToList();
				if (title.Length == 0 || list.Count == 0)
				{
					System.Windows.MessageBox.Show(w, "填写名称并勾选至少一个入口。");
				}
				else if (Prefs.Workspaces.Any((Workspace x) => x != current && x.Name.Equals(title, StringComparison.CurrentCultureIgnoreCase)))
				{
					System.Windows.MessageBox.Show(w, "工作区名称已存在。");
				}
				else
				{
					if (current == null)
					{
						current = new Workspace();
						Prefs.Workspaces.Add(current);
					}
					current.Name = title;
					current.Paths = list;
					Save();
					RefreshTrayMenu();
					Workspace selectedItem = current;
					reload();
					saved.SelectedItem = selectedItem;
				}
			}));
			wrapPanel.Children.Add(FeatureUi.Button("打开已选", delegate
			{
				Workspace work = new Workspace
				{
					Name = name.Text,
					Paths = (from x in picked
						where x.Value.IsChecked.GetValueOrDefault()
						select x.Key).ToList()
				};
				w.Close();
				LaunchWorkspace(work);
			}));
			wrapPanel.Children.Add(FeatureUi.Button("删除工作区", delegate
			{
				if (current != null)
				{
					Prefs.Workspaces.Remove(current);
					current = null;
					Save();
					RefreshTrayMenu();
					reload();
					fill(new string[0]);
				}
			}));
			w.ShowDialog();
		}
		finally
		{
			Board.ModalBusy = modalBusy;
		}
	}

	public async void LaunchWorkspace(Workspace work)
	{
		work.Normalize();
		Board.CollapseSmooth();
		RunningWindows.Scan();
		List<string> failures = new List<string>();
		foreach (string path in work.Paths)
		{
			if (System.Windows.Application.Current == null || System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
			{
				return;
			}
			try
			{
				if (!RunningWindows.IsRunning(Prefs.Apps.FirstOrDefault((Entry a) => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) ?? new Entry
				{
					Path = path,
					Name = Path.GetFileNameWithoutExtension(path)
				}))
				{
					Process.Start(new ProcessStartInfo(path)
					{
						UseShellExecute = true
					});
				}
			}
			catch
			{
				failures.Add(Path.GetFileName(path));
			}
			await Task.Delay(150);
		}
		if (failures.Count > 0)
		{
			System.Windows.MessageBox.Show("这些入口无法打开：\n" + string.Join("\n", failures), work.Name);
		}
	}

	public bool ManageSchemes(Window owner = null)
	{
		bool applied = false;
		bool modalBusy = Board.ModalBusy;
		Board.ModalBusy = true;
		try
		{
			Window w = FeatureUi.Dialog("外观方案", owner ?? OwnerWindow, 650.0, 480.0);
			DockPanel dockPanel = new DockPanel
			{
				Margin = new Thickness(22.0)
			};
			w.Content = dockPanel;
			TextBlock element = new TextBlock
			{
				Text = "保存配色、尺寸、位置、图标和应用布局；切换保留后来新增的入口。",
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
			};
			DockPanel.SetDock(element, System.Windows.Controls.Dock.Top);
			dockPanel.Children.Add(element);
			WrapPanel wrapPanel = new WrapPanel();
			DockPanel.SetDock(wrapPanel, System.Windows.Controls.Dock.Bottom);
			dockPanel.Children.Add(wrapPanel);
			System.Windows.Controls.ListBox list = new System.Windows.Controls.ListBox
			{
				Margin = new Thickness(0.0, 0.0, 0.0, 14.0),
				DisplayMemberPath = "Name"
			};
			dockPanel.Children.Add(list);
			Dictionary<AppearanceScheme, string> files = new Dictionary<AppearanceScheme, string>();
			Action reload = delegate
			{
				files.Clear();
				string[] array = SchemeStore.Files();
				foreach (string text2 in array)
				{
					try
					{
						AppearanceScheme key4 = SchemeStore.Load(text2);
						files[key4] = text2;
					}
					catch
					{
					}
				}
				list.ItemsSource = null;
				list.ItemsSource = files.Keys.OrderBy((AppearanceScheme s) => s.Name).ToList();
			};
			reload();
			wrapPanel.Children.Add(FeatureUi.Button("保存当前", delegate
			{
				string text = FeatureUi.Ask(w, "方案名称");
				if (text != null)
				{
					AppearanceScheme data = FeatureRules.Capture(Prefs, text);
					SchemeStore.Embed(data);
					SchemeStore.Save(data, Path.Combine(SchemeStore.Folder, Guid.NewGuid().ToString("N") + ".json"));
					reload();
				}
			}));
			wrapPanel.Children.Add(FeatureUi.Button("应用方案", delegate
			{
				if (!(list.SelectedItem is AppearanceScheme key3))
				{
					return;
				}
				try
				{
					AppearanceScheme appearanceScheme = SchemeStore.Load(files[key3]);
					SchemeStore.Materialize(appearanceScheme);
					FeatureRules.Apply(Prefs, appearanceScheme);
					editHistory.Clear();
					applied = true;
					Refresh();
					RefreshTrayMenu();
					Board.Hide();
					w.Close();
				}
				catch (Exception ex2)
				{
					System.Windows.MessageBox.Show(w, ex2.Message, "无法应用方案");
				}
			}));
			wrapPanel.Children.Add(FeatureUi.Button("导出", delegate
			{
				if (list.SelectedItem is AppearanceScheme key2)
				{
					Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
					{
						FileName = "GlassBoard_外观方案.json",
						Filter = "外观方案|*.json"
					};
					if (saveFileDialog.ShowDialog(w).GetValueOrDefault())
					{
						File.Copy(files[key2], saveFileDialog.FileName, overwrite: true);
					}
				}
			}));
			wrapPanel.Children.Add(FeatureUi.Button("导入", delegate
			{
				Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
				{
					Filter = "外观方案|*.json"
				};
				if (!openFileDialog.ShowDialog(w).GetValueOrDefault())
				{
					return;
				}
				try
				{
					SchemeStore.Save(SchemeStore.Load(openFileDialog.FileName), Path.Combine(SchemeStore.Folder, Guid.NewGuid().ToString("N") + ".json"));
					reload();
				}
				catch (Exception ex)
				{
					System.Windows.MessageBox.Show(w, ex.Message, "无法导入方案");
				}
			}));
			wrapPanel.Children.Add(FeatureUi.Button("删除方案", delegate
			{
				if (list.SelectedItem is AppearanceScheme key && System.Windows.MessageBox.Show(w, "删除保存的方案？当前外观保持。", "GlassBoard", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
				{
					File.Delete(files[key]);
					reload();
				}
			}));
			w.ShowDialog();
		}
		finally
		{
			Board.ModalBusy = modalBusy;
		}
		if (applied && settingsWindow != null && owner != settingsWindow)
		{
			settingsWindow.Close();
		}
		return applied;
	}

	public Controller()
		: this(desktop: true)
	{
	}

	internal Controller(bool desktop)
	{
		if (!desktop)
		{
			Prefs = new Preferences();
			Board = new BoardWindow(this);
			Dock = new DockWindow(this);
			return;
		}
		Prefs = Store.Load();
		// 老配置回填：补读快捷方式自带的图标（只做一次）。
		// 不补的话，Edge/Chrome「应用化网页」那类快捷方式会一直显示成空白纸。
		if (LaunchInfoService.BackfillShortcutIcons(Prefs))
		{
			Store.Save(Prefs);
		}
		Board = new BoardWindow(this);
		Dock = new DockWindow(this);
		Tray = new NotifyIcon
		{
			Text = "GlassBoard v0.9.4 · 玻璃启动台",
			Icon = (Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location) ?? SystemIcons.Application),
			Visible = true
		};
		RefreshTrayMenu();
		Tray.DoubleClick += delegate
		{
			Toggle(force: true);
		};
		Dock.Refresh();
		Dock.Show();
		if (Prefs.HotkeyMigrationConflict)
		{
			Tray.ShowBalloonTip(4500, "快捷键升级提示", "原有组合与 Ctrl+Alt+H 冲突，已保留原设置并关闭新增项。可在设置中录制新组合。", ToolTipIcon.Info);
			Save();
		}
		RefreshWidgets();
		if (!Prefs.Imported)
		{
			Toggle(force: true);
			Import();
		}
	}

	private string Shortcut(int id)
	{
		return HotkeyRules.Label(Prefs.Hotkeys.First((HotkeySetting k) => k.ActionId == id));
	}

	public void RefreshTrayMenu()
	{
		ContextMenuStrip contextMenuStrip = Tray.ContextMenuStrip;
		ContextMenuStrip contextMenuStrip2 = new ContextMenuStrip();
		contextMenuStrip2.Items.Add("打开应用面板   " + Shortcut(1), null, delegate
		{
			Toggle(force: true);
		});
		contextMenuStrip2.Items.Add("添加软件   " + Shortcut(4), null, delegate
		{
			AddFromAnywhere();
		});
		contextMenuStrip2.Items.Add("导入开始菜单应用", null, delegate
		{
			Import();
		});
		contextMenuStrip2.Items.Add("外观与快捷键   " + Shortcut(5), null, delegate
		{
			Settings();
		});
		contextMenuStrip2.Items.Add("隐藏 / 显示任务栏   " + Shortcut(2), null, delegate
		{
			ShellAction(1);
		});
		contextMenuStrip2.Items.Add("隐藏 / 显示桌面图标   " + Shortcut(3), null, delegate
		{
			ShellAction(2);
		});
		contextMenuStrip2.Items.Add("恢复任务栏与桌面图标   " + Shortcut(6), null, delegate
		{
			ShellAction(3);
		});
		contextMenuStrip2.Items.Add("Windows 任务栏自动隐藏   " + Shortcut(7), null, delegate
		{
			ToggleTaskbarAutoHide();
		});
		contextMenuStrip2.Items.Add("备忘录记事板", null, delegate
		{
			ShowNotes();
		});
		contextMenuStrip2.Items.Add("音频律动", null, delegate
		{
			ToggleVisualizer();
		});
		contextMenuStrip2.Items.Add("组件布局", null, delegate
		{
			ConfigureWidgets();
		});
		contextMenuStrip2.Items.Add("恢复并开启全部组件", null, delegate
		{
			RecoverWidgets();
		});
		string[] kinds = WidgetRules.Kinds;
		foreach (string text in kinds)
		{
			string kind = text;
			contextMenuStrip2.Items.Add("开启组件 · " + WidgetRules.Names[Array.IndexOf(WidgetRules.Kinds, kind)], null, delegate
			{
				SetWidgetEnabled(kind, value: true);
			});
		}
		contextMenuStrip2.Items.Add("工作区管理", null, delegate
		{
			ManageWorkspaces();
		});
		foreach (Workspace workspace2 in Prefs.Workspaces)
		{
			Workspace workspace = workspace2;
			contextMenuStrip2.Items.Add("打开工作区 · " + workspace.Name, null, delegate
			{
				LaunchWorkspace(workspace);
			});
		}
		contextMenuStrip2.Items.Add("外观方案", null, delegate
		{
			ManageSchemes();
		});
		contextMenuStrip2.Items.Add("打开配置文件夹", null, delegate
		{
			Process.Start(new ProcessStartInfo(Store.Folder)
			{
				UseShellExecute = true
			});
		});
		contextMenuStrip2.Items.Add(new ToolStripSeparator());
		contextMenuStrip2.Items.Add("退出 GlassBoard", null, delegate
		{
			Quit();
		});
		Tray.ContextMenuStrip = contextMenuStrip2;
		contextMenuStrip?.Dispose();
	}

	public void Save()
	{
		try
		{
			Store.Save(Prefs);
		}
		catch (Exception ex)
		{
			Store.Log(ex);
			System.Windows.MessageBox.Show("布局保存失败：" + ex.Message, "GlassBoard");
		}
	}

	public void Refresh()
	{
		Save();
		Dock.Refresh();
		Board.SyncGroups();
		Board.InvalidateFolders();
		Board.Render();
		RefreshWidgets();
	}

	public void Toggle(bool force = false)
	{
		if (Board.IsVisible && !force)
		{
			Board.CollapseSmooth();
		}
		else
		{
			Board.Open();
		}
	}

	public void Launch(Entry entry)
	{
		try
		{
			if (entry.IsFolder || Directory.Exists(entry.Path) || !RunningWindows.Activate(entry))
			{
				Process.Start(new ProcessStartInfo(entry.Path)
				{
					UseShellExecute = true
				});
			}
			Board.CollapseSmooth();
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show("无法打开「" + entry.Name + "」。\n入口可能已移动或卸载。\n\n" + ex.Message, "GlassBoard");
		}
	}

	public void Add(IEnumerable<string> paths, bool manual = true, string targetGroup = null)
	{
		if (manual && targetGroup == null && Board.IsVisible)
		{
			targetGroup = Board.CurrentGroup;
		}
		BoardEditState before = BeginBoardEdit();
		int num = 0;
		foreach (string path2 in paths)
		{
			string path = EntryPathRules.Normalize(path2);
			if ((!File.Exists(path) && !Directory.Exists(path)) || !LayoutRules.AllowImport(path, Prefs.Removed, manual))
			{
				continue;
			}
			Entry entry = Prefs.Apps.FirstOrDefault((Entry a) => string.Equals(EntryPathRules.Normalize(a.Path), path, StringComparison.OrdinalIgnoreCase));
			if (entry != null)
			{
				if (manual && targetGroup != null && targetGroup != "全部")
				{
					num += FeatureRules.AddShared(Prefs, new string[1] { entry.Path }, targetGroup);
				}
				continue;
			}
			if (manual)
			{
				Prefs.Removed.RemoveAll((string p) => string.Equals(EntryPathRules.Normalize(p), path, StringComparison.OrdinalIgnoreCase));
			}
			Entry entry2 = new Entry
			{
				Name = EntryPathRules.Name(path),
				Path = path,
				IsFolder = Directory.Exists(path),
				Manual = manual
			};
			LaunchInfoService.Enrich(entry2);
			if (manual || LaunchInfoService.Primary(entry2))
			{
				entry2.Group = FeatureRules.ImportGroup(Prefs, entry2, manual ? targetGroup : null);
				Prefs.Apps.Add(entry2);
				num++;
			}
		}
		if (num > 0)
		{
			EndBoardEdit(before, "添加入口");
			Refresh();
		}
	}

	public void Choose()
	{
		Board.ModalBusy = true;
		try
		{
			new BatchAddWindow(this).ShowDialog();
		}
		finally
		{
			Board.ModalBusy = false;
		}
	}

	public void AddFromAnywhere()
	{
		Toggle(force: true);
		Choose();
	}

	public void Opacity(double percent)
	{
		Prefs.OpacityPercent = LayoutRules.GlassOpacity(percent);
		Board.SetGlassOpacity(Prefs.OpacityPercent.Value);
	}

	public void Remove(Entry a, string scope = null)
	{
		RemoveMany(new string[1] { a.Path }, scope);
	}

	public void RemoveMany(IEnumerable<string> paths, string scope = null)
	{
		string group = scope ?? (Board.IsVisible ? Board.CurrentGroup : "全部");
		HashSet<string> targets = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
		targets.RemoveWhere((string path) => !Prefs.Apps.Any((Entry a) => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase) && FeatureRules.InGroup(a, group)));
		int num = Prefs.Apps.Count((Entry a) => targets.Contains(a.Path));
		if (num == 0)
		{
			return;
		}
		Board.ModalBusy = true;
		try
		{
			string messageBoxText = "从「" + group + "」移除已选的 " + num + " 个入口？\n" + ((group == "全部") ? "本次从所有分组与 Dock 移除，原软件和磁盘文件保留。" : "同一软件在其他组中的入口保留；原软件和磁盘文件保留。") + "\n移除后可点击「撤销」。";
			if ((Board.IsVisible ? System.Windows.MessageBox.Show(Board, messageBoxText, "GlassBoard", MessageBoxButton.YesNo) : System.Windows.MessageBox.Show(messageBoxText, "GlassBoard", MessageBoxButton.YesNo)) == MessageBoxResult.Yes)
			{
				BoardEditState before = BeginBoardEdit();
				EntryRules.Remove(Prefs, targets, group);
				EndBoardEdit(before, "移除入口");
				Board.FinishSelection();
				Refresh();
				Board.SetStatus("已移除 " + num + " 个入口 · 可撤销本次移除");
			}
		}
		finally
		{
			Board.ModalBusy = false;
		}
	}

	public void UndoRemoval()
	{
		UndoBoard();
	}

	public void Pin(Entry a)
	{
		if (!a.Pinned && Prefs.Apps.Count((Entry x) => x.Pinned) >= 20)
		{
			System.Windows.MessageBox.Show("最多固定 20 个入口。请先取消一个固定。", "GlassBoard");
			return;
		}
		BoardEditState before = BeginBoardEdit();
		a.Pinned = !a.Pinned;
		EndBoardEdit(before, "调整 Dock 固定");
		Refresh();
	}

	private static IEnumerable<string> Shortcuts(string root)
	{
		if (!Directory.Exists(root))
		{
			yield break;
		}
		Stack<string> pending = new Stack<string>();
		pending.Push(root);
		int count = 0;
		while (pending.Count > 0 && count < 2500)
		{
			string path = pending.Pop();
			string[] files;
			string[] dirs;
			try
			{
				files = Directory.GetFiles(path);
				dirs = Directory.GetDirectories(path);
			}
			catch
			{
				continue;
			}
			string[] array = files;
			foreach (string text in array)
			{
				switch (Path.GetExtension(text).ToLowerInvariant())
				{
				case ".lnk":
				case ".appref-ms":
				case ".url":
					if (StartupEntryRules.Candidate(text))
					{
						count++;
						yield return text;
					}
					break;
				}
			}
			string[] array2 = dirs;
			foreach (string text2 in array2)
			{
				try
				{
					if ((File.GetAttributes(text2) & FileAttributes.ReparsePoint) == 0)
					{
						pending.Push(text2);
					}
				}
				catch
				{
				}
			}
		}
	}

	public async void Import()
	{
		if (importing)
		{
			return;
		}
		importing = true;
		Board.SetStatus("正在读取开始菜单…");
		try
		{
			string[] paths = await Task.Run(() => Shortcuts(Environment.GetFolderPath(Environment.SpecialFolder.Programs)).Concat(Shortcuts(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms))).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy((string p) => Path.GetFileNameWithoutExtension(p), StringComparer.CurrentCultureIgnoreCase)
				.ToArray());
			int count = Prefs.Apps.Count;
			Add(paths, manual: false);
			int merged = MergeSimilarEntries();
			if (merged > 0)
			{
				Store.Save(Prefs);
			}
			Prefs.Imported = true;
			if (!Prefs.Apps.Any((Entry a) => a.Pinned))
			{
				string[] common = new string[12]
				{
					"edge", "chrome", "firefox", "微信", "wechat", "qq", "steam", "blender", "maya", "剪映",
					"capcut", "code"
				};
				foreach (Entry item in Prefs.Apps.Where((Entry a) => common.Any((string k) => a.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)).Take(7))
				{
					item.Pinned = true;
				}
			}
			Refresh();
			Board.SetStatus("已导入 " + (Prefs.Apps.Count - count) + " 个入口");
		}
		catch (Exception ex)
		{
			Store.Log(ex);
			Board.SetStatus("导入失败，可使用右上角添加");
		}
		finally
		{
			importing = false;
		}
	}

	public System.Windows.Controls.ContextMenu Menu(Entry a, string scope = "全部")
	{
		System.Windows.Controls.ContextMenu menu = new System.Windows.Controls.ContextMenu();
		Action<string, Action> action2 = delegate(string title, Action action)
		{
			System.Windows.Controls.MenuItem menuItem3 = new System.Windows.Controls.MenuItem
			{
				Header = title
			};
			menuItem3.Click += delegate
			{
				action();
			};
			menu.Items.Add(menuItem3);
		};
		action2("打开", delegate
		{
			Launch(a);
		});
		action2(a.Pinned ? "取消 Dock 固定" : "固定到 Dock", delegate
		{
			Pin(a);
		});
		action2("前移一位", delegate
		{
			int num2 = Prefs.Apps.IndexOf(a);
			if (num2 > 0)
			{
				Prefs.Apps.RemoveAt(num2);
				Prefs.Apps.Insert(num2 - 1, a);
				Refresh();
			}
		});
		action2("后移一位", delegate
		{
			int num = Prefs.Apps.IndexOf(a);
			if (num < Prefs.Apps.Count - 1)
			{
				Prefs.Apps.RemoveAt(num);
				Prefs.Apps.Insert(num + 1, a);
				Refresh();
			}
		});
		action2("修改名称与图标", delegate
		{
			Customize(a);
		});
		System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
		{
			Header = "移动到分组"
		};
		foreach (string item in new string[1] { "未分组" }.Concat(Prefs.Groups))
		{
			string name = item;
			System.Windows.Controls.MenuItem menuItem2 = new System.Windows.Controls.MenuItem
			{
				Header = name
			};
			menuItem2.Click += delegate
			{
				TransferEntries(new string[1] { a.Path }, scope, name);
			};
			menuItem.Items.Add(menuItem2);
		}
		menu.Items.Add(menuItem);
		action2("移除入口", delegate
		{
			Remove(a, scope);
		});
		return menu;
	}

	internal static ToggleButton VisibilitySwitch()
	{
		ToggleButton obj = new ToggleButton
		{
			Width = 50.0,
			Height = 26.0,
			Cursor = System.Windows.Input.Cursors.Hand,
			VerticalAlignment = VerticalAlignment.Center
		};
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(Border));
		frameworkElementFactory.Name = "Track";
		frameworkElementFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(13.0));
		frameworkElementFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(83, 87, 99)));
		FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(Border));
		frameworkElementFactory2.Name = "Thumb";
		frameworkElementFactory2.SetValue(FrameworkElement.WidthProperty, 20.0);
		frameworkElementFactory2.SetValue(FrameworkElement.HeightProperty, 20.0);
		frameworkElementFactory2.SetValue(FrameworkElement.MarginProperty, new Thickness(3.0));
		frameworkElementFactory2.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Left);
		frameworkElementFactory2.SetValue(Border.CornerRadiusProperty, new CornerRadius(10.0));
		frameworkElementFactory2.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.White);
		frameworkElementFactory.AppendChild(frameworkElementFactory2);
		ControlTemplate controlTemplate = new ControlTemplate(typeof(ToggleButton))
		{
			VisualTree = frameworkElementFactory
		};
		Trigger trigger = new Trigger
		{
			Property = ToggleButton.IsCheckedProperty,
			Value = true
		};
		trigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 162, 238)), "Track"));
		trigger.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Right, "Thumb"));
		controlTemplate.Triggers.Add(trigger);
		obj.Template = controlTemplate;
		return obj;
	}

	public void Settings(int selectedPage = -1)
	{
		if (settingsOpen)
		{
			if (settingsWindow != null)
			{
				if (selectedPage >= 0 && settingsTabs != null)
				{
					settingsTabs.SelectedIndex = Math.Max(0, Math.Min(5, selectedPage));
				}
				if (settingsWindow.WindowState == WindowState.Minimized)
				{
					settingsWindow.WindowState = WindowState.Normal;
				}
				settingsWindow.Activate();
			}
			return;
		}
		settingsOpen = true;
		DispatcherTimer preview = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(100.0)
		};
		DispatcherTimer stateTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(700.0)
		};
		bool flag = false;
		bool cleaned = false;
		Action cleanup = delegate
		{
			if (cleaned)
			{
				return;
			}
			cleaned = true;
			settingsOpen = false;
			settingsWindow = null;
			settingsTabs = null;
			preview.Stop();
			stateTimer.Stop();
			Save();
			try
			{
				if (!ShuttingDown)
				{
					Dock.Refresh();
					RefreshWidgets();
				}
			}
			catch (Exception ex4)
			{
				Store.Log(ex4);
			}
		};
		try
		{
			Board.Hide();
			Window w = new Window
			{
				Title = "GlassBoard · 个性化与控制 v0.9.4",
				Width = Math.Min(940.0, SystemParameters.WorkArea.Width - 40.0),
				Height = Math.Min(790.0, SystemParameters.WorkArea.Height - 50.0),
				MinWidth = 560.0,
				ResizeMode = ResizeMode.CanResize,
				WindowStartupLocation = WindowStartupLocation.CenterScreen,
				Owner = Dock,
				Topmost = true,
				Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 30, 38)),
				Foreground = System.Windows.Media.Brushes.White
			};
			settingsWindow = w;
			w.Closed += delegate
			{
				cleanup();
			};
			Rect workArea = SystemParameters.WorkArea;
			double[] array = SettingsPlacementRules.Vertical(workArea.Top, workArea.Height, Dock.RestingTop, Dock.Height, 790.0);
			w.WindowStartupLocation = WindowStartupLocation.Manual;
			w.Left = workArea.Left + (workArea.Width - w.Width) / 2.0;
			w.Top = array[0];
			w.Height = array[1];
			Polish.Apply(w, light: false);
			Grid grid = new Grid
			{
				Margin = new Thickness(24.0)
			};
			grid.RowDefinitions.Add(new RowDefinition
			{
				Height = GridLength.Auto
			});
			grid.RowDefinitions.Add(new RowDefinition());
			grid.RowDefinitions.Add(new RowDefinition
			{
				Height = GridLength.Auto
			});
			w.Content = grid;
			StackPanel stackPanel = new StackPanel();
			stackPanel.Children.Add(new TextBlock
			{
				Text = "把桌面，变成你的空间",
				FontSize = 25.0,
				Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
			});
			stackPanel.Children.Add(new TextBlock
			{
				Text = "GLASSBOARD  /  个性化、组件与桌面控制",
				Foreground = System.Windows.Media.Brushes.LightGray,
				Margin = new Thickness(0.0, 0.0, 0.0, 18.0)
			});
			grid.Children.Add(stackPanel);
			System.Windows.Controls.TabControl tabs = new System.Windows.Controls.TabControl
			{
				Background = System.Windows.Media.Brushes.Transparent,
				BorderThickness = new Thickness(0.0),
				TabStripPlacement = System.Windows.Controls.Dock.Left
			};
			Grid.SetRow(tabs, 1);
			grid.Children.Add(tabs);
			settingsTabs = tabs;
			Func<string, StackPanel> func = delegate(string heading)
			{
				Border border = Polish.Section(heading, (heading == "赞赏 Miya") ? "感谢每一份支持，让创作持续生长。" : "调整会即时预览；关闭窗口保留外观与组件设置。", light: false);
				StackPanel obj4 = (StackPanel)border.Child;
				TextElement.SetForeground(obj4, System.Windows.Media.Brushes.White);
				StackPanel stackPanel10 = new StackPanel
				{
					Margin = new Thickness(10.0, 8.0, 0.0, 8.0)
				};
				stackPanel10.Children.Add(border);
				TabItem newItem = new TabItem
				{
					Header = heading,
					Content = new ScrollViewer
					{
						Content = stackPanel10,
						VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
						HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
					}
				};
				tabs.Items.Add(newItem);
				return obj4;
			};
			StackPanel stackPanel2 = func("面板与主题");
			StackPanel stackPanel3 = func("Dock DIY");
			StackPanel stackPanel4 = func("快捷键与开关");
			StackPanel stackPanel5 = func("桌面组件");
			StackPanel stackPanel6 = func("通用");
			StackPanel creatorPage = func("赞赏 Miya");
			bool creatorLoaded = false;
			Action loadCreator = delegate
			{
				if (!creatorLoaded)
				{
					BuildCreatorSupport(creatorPage, w);
					creatorLoaded = true;
				}
			};
			tabs.SelectionChanged += delegate
			{
				if (tabs.SelectedIndex == 5)
				{
					loadCreator();
				}
			};
			tabs.SelectedIndex = Math.Max(0, Math.Min(5, selectedPage));
			if (tabs.SelectedIndex == 5)
			{
				loadCreator();
			}
			DockOptions draft = Prefs.Dock.Copy();
			bool loading = false;
			preview.Tick += delegate
			{
				preview.Stop();
				Dock.Refresh(capture: false);
			};
			Action live = delegate
			{
				if (!loading)
				{
					draft.Normalize();
					Prefs.Dock = draft.Copy();
					if (!preview.IsEnabled)
					{
						preview.Start();
					}
				}
			};
			Func<StackPanel, string, double, double, double, double, string, Action<double>, Slider> func2 = delegate(StackPanel panel, string label, double min, double max, double value, double tick, string unit, Action<double> change)
			{
				TextBlock caption = new TextBlock
				{
					Text = label + "  " + value.ToString("0.##") + unit,
					Margin = new Thickness(0.0, 12.0, 0.0, 6.0),
					Foreground = System.Windows.Media.Brushes.White
				};
				panel.Children.Add(caption);
				Slider control2 = new Slider
				{
					Minimum = min,
					Maximum = max,
					Value = value,
					TickFrequency = tick,
					IsSnapToTickEnabled = true,
					Margin = new Thickness(0.0, 0.0, 5.0, 4.0)
				};
				panel.Children.Add(control2);
				control2.ValueChanged += delegate
				{
					caption.Text = label + "  " + control2.Value.ToString("0.##") + unit;
					change(control2.Value);
				};
				return control2;
			};
			Func<StackPanel, string, bool, Action<bool>, System.Windows.Controls.CheckBox> func3 = delegate(StackPanel panel, string label, bool value, Action<bool> change)
			{
				System.Windows.Controls.CheckBox checkBox = new System.Windows.Controls.CheckBox
				{
					Content = label,
					IsChecked = value,
					Foreground = System.Windows.Media.Brushes.White,
					Margin = new Thickness(0.0, 10.0, 0.0, 8.0)
				};
				panel.Children.Add(checkBox);
				checkBox.Checked += delegate
				{
					change(obj: true);
				};
				checkBox.Unchecked += delegate
				{
					change(obj: false);
				};
				return checkBox;
			};
			Func<StackPanel, string, string[], int, Action<int>, System.Windows.Controls.ComboBox> func4 = delegate(StackPanel panel, string label, string[] items, int value, Action<int> change)
			{
				panel.Children.Add(new TextBlock
				{
					Text = label,
					Margin = new Thickness(0.0, 12.0, 0.0, 6.0)
				});
				System.Windows.Controls.ComboBox control = new System.Windows.Controls.ComboBox
				{
					ItemsSource = items,
					SelectedIndex = value,
					Height = 32.0
				};
				panel.Children.Add(control);
				control.SelectionChanged += delegate
				{
					if (control.SelectedIndex >= 0)
					{
						change(control.SelectedIndex);
					}
				};
				return control;
			};
			func3(stackPanel2, "使用浅色玻璃", Prefs.Light, delegate(bool value)
			{
				Prefs.Light = value;
				live();
			});
			func4(stackPanel2, "大面板尺寸", new string[3] { "舒适 · 中号", "宽阔 · 大号", "铺满 · 全屏" }, Prefs.PanelSize, delegate(int value)
			{
				Prefs.PanelSize = value;
				Prefs.Placement.PanelWidth = (Prefs.Placement.PanelHeight = null);
			});
			func2(stackPanel2, "大面板透明度", 5.0, 90.0, 100.0 - Prefs.OpacityPercent.GetValueOrDefault(72.0), 5.0, "%", delegate(double value)
			{
				Opacity(100.0 - value);
			});
			func3(stackPanel2, "启用界面动效", draft.MotionEnabled, delegate(bool value)
			{
				draft.MotionEnabled = value;
				live();
			});
			func2(stackPanel2, "动效强度", 0.5, 1.5, draft.MotionLevel, 0.1, " 倍", delegate(double value)
			{
				draft.MotionLevel = value;
				live();
			});
			func2(stackPanel2, "玻璃模糊", 0.0, 40.0, draft.BlurRadius, 2.0, "", delegate(double value)
			{
				draft.BlurRadius = value;
				live();
			});
			func2(stackPanel2, "边缘高光", 0.2, 2.0, draft.EdgeStrength, 0.1, " 倍", delegate(double value)
			{
				draft.EdgeStrength = value;
				live();
			});
			func3(stackPanel2, "锁定大面板位置和尺寸", Prefs.Placement.PanelLocked, delegate(bool value)
			{
				Prefs.Placement.PanelLocked = value;
			});
			func3(stackPanel2, "新增软件自动识别分组", Prefs.AutoGrouping, delegate(bool value)
			{
				Prefs.AutoGrouping = value;
				Save();
			});
			func3(stackPanel2, "显示「未分组」分类（关掉后只留「全部」）", Prefs.ShowUngrouped, delegate(bool value)
			{
				Prefs.ShowUngrouped = value;
				Save();
				Refresh();
			});
			WrapPanel wrapPanel = new WrapPanel
			{
				Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
			};
			stackPanel2.Children.Add(wrapPanel);
			wrapPanel.Children.Add(FeatureUi.Button("外观方案", delegate
			{
				Save();
				if (ManageSchemes(w))
				{
					draft = Prefs.Dock.Copy();
					w.Close();
				}
			}));
			wrapPanel.Children.Add(FeatureUi.Button("管理分组", delegate
			{
				ManageGroups(w);
			}));
			wrapPanel.Children.Add(FeatureUi.Button("创作工作区", delegate
			{
				ManageWorkspaces(w);
			}));
			wrapPanel.Children.Add(FeatureUi.Button("自动分组未分组软件", delegate
			{
				AutoGroupApps(w);
			}));
			wrapPanel.Children.Add(FeatureUi.Button("重置面板位置和尺寸", delegate
			{
				Prefs.Placement.PanelLeft = (Prefs.Placement.PanelTop = (Prefs.Placement.PanelWidth = (Prefs.Placement.PanelHeight = null)));
				Save();
			}));
			stackPanel2.Children.Add(new TextBlock
			{
				Text = "面板平滑展开、图标逐项淡入、悬停轻抬；关闭动效后直接呈现。",
				TextWrapping = TextWrapping.Wrap,
				Foreground = System.Windows.Media.Brushes.LightGray,
				Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
			});
			stackPanel3.Children.Add(new TextBlock
			{
				Text = "独立定制下方玻璃板",
				FontSize = 18.0,
				Foreground = System.Windows.Media.Brushes.White
			});
			System.Windows.Controls.ComboBox presets = func4(stackPanel3, "一键风格", new string[4] { "自定义", "紧凑", "均衡", "宽松" }, 0, delegate
			{
			});
			Slider icon = func2(stackPanel3, "图标尺寸", 32.0, 64.0, draft.IconSize, 2.0, " px", delegate(double value)
			{
				draft.IconSize = value;
				live();
			});
			Slider spacing = func2(stackPanel3, "图标间距", 0.0, 20.0, draft.Spacing, 1.0, " px", delegate(double value)
			{
				draft.Spacing = value;
				live();
			});
			Slider padding = func2(stackPanel3, "玻璃板内边距", 8.0, 28.0, draft.Padding, 1.0, " px", delegate(double value)
			{
				draft.Padding = value;
				live();
			});
			Slider radius = func2(stackPanel3, "圆角", 0.0, 48.0, draft.Radius, 2.0, " px", delegate(double value)
			{
				draft.Radius = value;
				live();
			});
			func4(stackPanel3, "Dock 停靠位置", new string[3] { "贴底部（横向）", "贴左侧（竖向）", "贴右侧（竖向）" }, draft.Edge, delegate(int value)
			{
				draft.Edge = value;
				Prefs.Placement.DockLeft = null;
				Prefs.Placement.DockTop = null;
				live();
			});
			func4(stackPanel3, "底板样式", new string[2] { "玻璃底板", "纯透明（不显示底板）" }, draft.Background, delegate(int value)
			{
				draft.Background = value;
				live();
			});
			Slider opacity = func2(stackPanel3, "Dock 透明度", 5.0, 90.0, 100.0 - draft.OpacityPercent, 5.0, "%", delegate(double value)
			{
				draft.OpacityPercent = 100.0 - value;
				live();
			});
			Slider zoom = func2(stackPanel3, "悬停放大", 1.0, 1.6, draft.Zoom, 0.05, " 倍", delegate(double value)
			{
				draft.Zoom = value;
				live();
			});
			System.Windows.Controls.CheckBox labels = func3(stackPanel3, "显示图标名称", draft.ShowLabels, delegate(bool value)
			{
				draft.ShowLabels = value;
				live();
			});
			func3(stackPanel3, "显示此电脑", draft.ShowComputer, delegate(bool value)
			{
				draft.ShowComputer = value;
				live();
			});
			func4(stackPanel3, "此电脑图标样式", new string[2] { "渐变徽章", "简洁线框" }, draft.ComputerStyle, delegate(int value)
			{
				draft.ComputerStyle = value;
				live();
			});
			func4(stackPanel3, "强调色", new string[6] { "晴空蓝", "雾紫", "薄荷绿", "暖金", "玫瑰粉", "月光灰" }, draft.Accent, delegate(int value)
			{
				draft.Accent = value;
				live();
			});
			func3(stackPanel3, "显示分隔线", draft.ShowSeparator, delegate(bool value)
			{
				draft.ShowSeparator = value;
				live();
			});
			func3(stackPanel3, "Dock 保持在窗口最上方", draft.AlwaysOnTop, delegate(bool value)
			{
				draft.AlwaysOnTop = value;
				live();
			});
			func3(stackPanel3, "显示软件运行状态", draft.ShowRunning, delegate(bool value)
			{
				draft.ShowRunning = value;
				live();
			});
			func3(stackPanel3, "Dock 自动隐藏，靠近停靠边唤出", draft.AutoHide, delegate(bool value)
			{
				draft.AutoHide = value;
				live();
			});
			func2(stackPanel3, "自动隐藏延迟", 200.0, 3000.0, draft.HideDelay, 100.0, " ms", delegate(double value)
			{
				draft.HideDelay = value;
				live();
			});
			func2(stackPanel3, "底边触发范围", 3.0, 48.0, draft.EdgeRange, 1.0, " px", delegate(double value)
			{
				draft.EdgeRange = value;
				live();
			});
			func3(stackPanel3, "锁定 Dock 位置（锁定时不能拖动）", Prefs.Placement.DockLocked, delegate(bool value)
			{
				Prefs.Placement.DockLocked = value;
				live();
			});
			stackPanel3.Children.Add(FeatureUi.Button("重置 Dock 位置", delegate
			{
				Prefs.Placement.DockLeft = (Prefs.Placement.DockTop = null);
				live();
			}));
			func4(stackPanel3, "水平位置", new string[3] { "靠左", "居中", "靠右" }, draft.Alignment, delegate(int value)
			{
				draft.Alignment = value;
				Prefs.Placement.DockLeft = null;
				live();
			});
			func2(stackPanel3, "左右微调", -500.0, 500.0, draft.Offset, 10.0, " px", delegate(double value)
			{
				draft.Offset = value;
				Prefs.Placement.DockLeft = null;
				live();
			});
			func2(stackPanel3, "距工作区底边", 0.0, 160.0, Prefs.DockGap, 2.0, " px", delegate(double value)
			{
				Prefs.DockGap = value;
				Prefs.Placement.DockTop = null;
				live();
			});
			func2(stackPanel3, "固定应用显示数量", 3.0, 20.0, draft.MaxPinned, 1.0, " 个", delegate(double value)
			{
				draft.MaxPinned = (int)value;
				live();
			});
			stackPanel3.Children.Add(new TextBlock
			{
				Text = "可固定 20 个入口。数量超限或屏幕放不下时，其余入口收进「更多」。",
				TextWrapping = TextWrapping.Wrap,
				Foreground = System.Windows.Media.Brushes.LightGray,
				Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
			});
			presets.SelectionChanged += delegate
			{
				if (presets.SelectedIndex >= 1)
				{
					int selectedIndex = presets.SelectedIndex;
					loading = true;
					icon.Value = selectedIndex switch
					{
						3 => 56, 
						1 => 34, 
						_ => 44, 
					};
					spacing.Value = selectedIndex switch
					{
						3 => 10, 
						1 => 2, 
						_ => 6, 
					};
					padding.Value = selectedIndex switch
					{
						3 => 22, 
						1 => 10, 
						_ => 16, 
					};
					radius.Value = selectedIndex switch
					{
						3 => 36, 
						1 => 20, 
						_ => 28, 
					};
					opacity.Value = selectedIndex switch
					{
						3 => 22, 
						1 => 35, 
						_ => 28, 
					};
					zoom.Value = selectedIndex switch
					{
						3 => 1.3, 
						1 => 1.1, 
						_ => 1.2, 
					};
					labels.IsChecked = selectedIndex == 3;
					loading = false;
					live();
				}
			};
			stackPanel4.Children.Add(new TextBlock
			{
				Text = "快捷键与桌面开关",
				FontSize = 18.0
			});
			stackPanel4.Children.Add(new TextBlock
			{
				Text = "可点击录制直接按组合键，保存后生效。右侧开关即时操作。\n隐藏开关：开启隐藏，关闭显示；自动开关：Windows 任务栏自动隐藏。",
				Foreground = System.Windows.Media.Brushes.LightGray,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 8.0, 0.0, 18.0)
			});
			Dictionary<string, uint> keyChoices = HotkeyRules.Keys();
			Dictionary<int, Tuple<System.Windows.Controls.CheckBox, System.Windows.Controls.ComboBox, System.Windows.Controls.ComboBox>> controls = new Dictionary<int, Tuple<System.Windows.Controls.CheckBox, System.Windows.Controls.ComboBox, System.Windows.Controls.ComboBox>>();
			Dictionary<int, ToggleButton> switches = new Dictionary<int, ToggleButton>();
			Dictionary<int, TextBlock> info = new Dictionary<int, TextBlock>();
			bool syncing = false;
			foreach (HotkeySetting binding in Prefs.Hotkeys.OrderBy((HotkeySetting k) => k.ActionId))
			{
				int id = binding.ActionId;
				StackPanel stackPanel7 = new StackPanel
				{
					Margin = new Thickness(0.0, 0.0, 0.0, 14.0)
				};
				stackPanel4.Children.Add(stackPanel7);
				Grid grid2 = new Grid();
				double[] array2 = new double[4] { 1.7, 1.7, 0.8, 1.1 };
				foreach (double value2 in array2)
				{
					grid2.ColumnDefinitions.Add(new ColumnDefinition
					{
						Width = new GridLength(value2, GridUnitType.Star)
					});
				}
				grid2.ColumnDefinitions.Insert(3, new ColumnDefinition
				{
					Width = new GridLength(64.0)
				});
				stackPanel7.Children.Add(grid2);
				System.Windows.Controls.CheckBox enabled = new System.Windows.Controls.CheckBox
				{
					Content = new TextBlock
					{
						Text = HotkeyRules.Actions[id - 1],
						TextWrapping = TextWrapping.Wrap
					},
					IsChecked = binding.Enabled,
					Foreground = System.Windows.Media.Brushes.White,
					VerticalAlignment = VerticalAlignment.Center,
					Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
				};
				grid2.Children.Add(enabled);
				System.Windows.Controls.ComboBox modifiers = new System.Windows.Controls.ComboBox
				{
					ItemsSource = HotkeyRules.ModifierNames,
					SelectedIndex = Array.IndexOf(HotkeyRules.ModifierValues, binding.Modifiers),
					Height = 32.0,
					Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
					IsEnabled = binding.Enabled
				};
				Grid.SetColumn(modifiers, 1);
				grid2.Children.Add(modifiers);
				System.Windows.Controls.ComboBox key = new System.Windows.Controls.ComboBox
				{
					ItemsSource = keyChoices.Keys.ToList(),
					SelectedItem = keyChoices.First((KeyValuePair<string, uint> k) => k.Value == binding.Key).Key,
					Height = 32.0,
					Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
					IsEnabled = binding.Enabled
				};
				Grid.SetColumn(key, 2);
				grid2.Children.Add(key);
				enabled.Checked += delegate
				{
					modifiers.IsEnabled = true;
					key.IsEnabled = true;
				};
				enabled.Unchecked += delegate
				{
					modifiers.IsEnabled = false;
					key.IsEnabled = false;
				};
				controls[id] = Tuple.Create(enabled, modifiers, key);
				System.Windows.Controls.Button button = new System.Windows.Controls.Button
				{
					Content = "录制",
					Height = 32.0,
					Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
				};
				Grid.SetColumn(button, 3);
				grid2.Children.Add(button);
				button.Click += delegate
				{
					List<HotkeySetting> keys = Dock.SuspendHotkeys();
					HotkeySetting captured;
					try
					{
						captured = FeatureUi.Record(w, id);
					}
					finally
					{
						Dock.ResumeHotkeys(keys);
					}
					if (captured != null)
					{
						enabled.IsChecked = true;
						modifiers.SelectedIndex = Array.IndexOf(HotkeyRules.ModifierValues, captured.Modifiers);
						key.SelectedItem = keyChoices.First((KeyValuePair<string, uint> x) => x.Value == captured.Key).Key;
					}
				};
				if (id == 2 || id == 3 || id == 7)
				{
					StackPanel stackPanel8 = new StackPanel
					{
						Orientation = System.Windows.Controls.Orientation.Horizontal,
						VerticalAlignment = VerticalAlignment.Center
					};
					Grid.SetColumn(stackPanel8, 4);
					grid2.Children.Add(stackPanel8);
					ToggleButton toggleButton = VisibilitySwitch();
					if (id == 7)
					{
						try
						{
							toggleButton.IsChecked = TaskbarAutoHide.Enabled;
						}
						catch
						{
							toggleButton.IsChecked = false;
						}
					}
					else
					{
						toggleButton.IsChecked = DesktopVisibility.DesiredHidden(id == 2);
					}
					toggleButton.ToolTip = ((id == 7) ? "开启 Windows 任务栏自动隐藏" : "开启隐藏，关闭显示");
					stackPanel8.Children.Add(toggleButton);
					stackPanel8.Children.Add(new TextBlock
					{
						Text = ((id == 7) ? "自动" : "隐藏"),
						Margin = new Thickness(6.0, 0.0, 0.0, 0.0),
						VerticalAlignment = VerticalAlignment.Center,
						Foreground = System.Windows.Media.Brushes.White
					});
					switches[id] = toggleButton;
					toggleButton.Checked += delegate
					{
						if (!syncing)
						{
							if (id == 7)
							{
								ToggleTaskbarAutoHide(true);
							}
							else
							{
								ShellAction(id - 1, true);
							}
						}
					};
					toggleButton.Unchecked += delegate
					{
						if (!syncing)
						{
							if (id == 7)
							{
								ToggleTaskbarAutoHide(false);
							}
							else
							{
								ShellAction(id - 1, false);
							}
						}
					};
				}
				TextBlock textBlock = new TextBlock
				{
					Foreground = System.Windows.Media.Brushes.LightGray,
					FontSize = 11.0,
					TextWrapping = TextWrapping.Wrap,
					Margin = new Thickness(0.0, 6.0, 0.0, 0.0)
				};
				stackPanel7.Children.Add(textBlock);
				info[id] = textBlock;
			}
			Action updateState = delegate
			{
				syncing = true;
				foreach (HotkeySetting hotkey in Prefs.Hotkeys)
				{
					string text2 = ((!hotkey.Enabled) ? "快捷键已关闭" : (Dock.HotkeyActive(hotkey.ActionId) ? "快捷键已注册" : "快捷键未注册，请换组合或重新注册"));
					if (hotkey.ActionId == 2 || hotkey.ActionId == 3)
					{
						bool taskbar = hotkey.ActionId == 2;
						bool? flag3 = DesktopVisibility.IsHidden(taskbar);
						text2 = text2 + " · 实际状态：" + ((!flag3.HasValue) ? "未找到系统窗口" : (flag3.Value ? "隐藏" : "显示"));
						switches[hotkey.ActionId].IsChecked = DesktopVisibility.DesiredHidden(taskbar);
					}
					if (hotkey.ActionId == 7)
					{
						try
						{
							bool enabled2 = TaskbarAutoHide.Enabled;
							switches[7].IsChecked = enabled2;
							text2 = text2 + " · 自动隐藏：" + (enabled2 ? "开启" : "关闭");
						}
						catch
						{
							text2 += " · 未找到 Windows 任务栏";
						}
					}
					info[hotkey.ActionId].Text = text2;
				}
				syncing = false;
			};
			stateTimer.Tick += delegate
			{
				updateState();
			};
			updateState();
			stateTimer.Start();
			WrapPanel wrapPanel2 = new WrapPanel();
			stackPanel4.Children.Add(wrapPanel2);
			System.Windows.Controls.Button button2 = new System.Windows.Controls.Button
			{
				Content = "恢复默认快捷键",
				Padding = new Thickness(12.0, 7.0, 12.0, 7.0),
				Margin = new Thickness(0.0, 0.0, 8.0, 6.0)
			};
			wrapPanel2.Children.Add(button2);
			button2.Click += delegate
			{
				foreach (HotkeySetting j in HotkeyRules.Defaults())
				{
					Tuple<System.Windows.Controls.CheckBox, System.Windows.Controls.ComboBox, System.Windows.Controls.ComboBox> tuple = controls[j.ActionId];
					tuple.Item1.IsChecked = true;
					tuple.Item2.SelectedIndex = Array.IndexOf(HotkeyRules.ModifierValues, j.Modifiers);
					tuple.Item3.SelectedItem = keyChoices.First((KeyValuePair<string, uint> x) => x.Value == j.Key).Key;
				}
			};
			System.Windows.Controls.Button button3 = new System.Windows.Controls.Button
			{
				Content = "重新注册当前快捷键",
				Padding = new Thickness(12.0, 7.0, 12.0, 7.0),
				Margin = new Thickness(0.0, 0.0, 8.0, 6.0)
			};
			wrapPanel2.Children.Add(button3);
			button3.Click += delegate
			{
				System.Windows.MessageBox.Show(w, Dock.RetryHotkeys(), "快捷键注册结果");
				updateState();
			};
			System.Windows.Controls.Button button4 = new System.Windows.Controls.Button
			{
				Content = "显示任务栏与桌面图标",
				Padding = new Thickness(12.0, 7.0, 12.0, 7.0),
				Margin = new Thickness(0.0, 0.0, 8.0, 6.0)
			};
			wrapPanel2.Children.Add(button4);
			button4.Click += delegate
			{
				ShellAction(3);
				updateState();
			};
			stackPanel4.Children.Add(new TextBlock
			{
				Text = "隐藏状态仅在本次运行中维持；正常退出恢复启动前状态。组合键占用不会阻止直接点击开关。",
				TextWrapping = TextWrapping.Wrap,
				Foreground = System.Windows.Media.Brushes.LightGray,
				Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
			});
			stackPanel5.Children.Add(new TextBlock
			{
				Text = "可选组件 · 默认关闭",
				FontSize = 18.0
			});
			Dictionary<string, System.Windows.Controls.ComboBox> routes = new Dictionary<string, System.Windows.Controls.ComboBox>();
			Dictionary<string, ToggleButton> widgetChecks = new Dictionary<string, ToggleButton>();
			bool routeSync = false;
			Action syncRoutes = delegate
			{
				routeSync = true;
				foreach (KeyValuePair<string, System.Windows.Controls.ComboBox> item in routes)
				{
					item.Value.SelectedIndex = WidgetRules.Panel(Prefs.Widgets, item.Key);
				}
				routeSync = false;
			};
			WrapPanel wrapPanel3 = new WrapPanel
			{
				Margin = new Thickness(0.0, 6.0, 0.0, 14.0)
			};
			stackPanel5.Children.Add(wrapPanel3);
			wrapPanel3.Children.Add(FeatureUi.Button("全部融合", delegate
			{
				Prefs.Widgets.Fuse();
				syncRoutes();
				RefreshWidgets();
				Save();
			}));
			wrapPanel3.Children.Add(FeatureUi.Button("全部分离", delegate
			{
				Prefs.Widgets.Separate();
				syncRoutes();
				RefreshWidgets();
				Save();
			}));
			stackPanel5.Children.Add(new TextBlock
			{
				Text = "组件选择同一个面板会融合，选择不同面板会分离。也可点击组件右上角 ••• 调整。",
				TextWrapping = TextWrapping.Wrap,
				Foreground = System.Windows.Media.Brushes.LightGray,
				Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
			});
			string[] kinds = WidgetRules.Kinds;
			foreach (string text in kinds)
			{
				string kind = text;
				int num = Array.IndexOf(WidgetRules.Kinds, kind);
				Grid grid3 = new Grid
				{
					Margin = new Thickness(0.0, 4.0, 0.0, 9.0)
				};
				grid3.ColumnDefinitions.Add(new ColumnDefinition());
				grid3.ColumnDefinitions.Add(new ColumnDefinition
				{
					Width = new GridLength(132.0)
				});
				StackPanel stackPanel9 = new StackPanel
				{
					Orientation = System.Windows.Controls.Orientation.Horizontal,
					VerticalAlignment = VerticalAlignment.Center
				};
				grid3.Children.Add(stackPanel9);
				ToggleButton enable = VisibilitySwitch();
				enable.IsChecked = WidgetRules.Enabled(Prefs.Widgets, kind);
				enable.ToolTip = "开启 / 关闭" + WidgetRules.Names[num];
				stackPanel9.Children.Add(enable);
				stackPanel9.Children.Add(new TextBlock
				{
					Text = WidgetRules.Names[num],
					Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
					Foreground = System.Windows.Media.Brushes.White,
					VerticalAlignment = VerticalAlignment.Center
				});
				widgetChecks[kind] = enable;
				enable.Click += delegate
				{
					bool valueOrDefault = enable.IsChecked.GetValueOrDefault();
					SetWidgetEnabled(kind, valueOrDefault);
				};
				System.Windows.Controls.ComboBox route = new System.Windows.Controls.ComboBox
				{
					ItemsSource = new string[4] { "面板 A", "面板 B", "面板 C", "面板 D" },
					SelectedIndex = WidgetRules.Panel(Prefs.Widgets, kind),
					Height = 34.0
				};
				Grid.SetColumn(route, 1);
				grid3.Children.Add(route);
				routes[kind] = route;
				route.SelectionChanged += delegate
				{
					if (!routeSync && route.SelectedIndex >= 0)
					{
						WidgetRules.Assign(Prefs.Widgets, kind, route.SelectedIndex);
						RefreshWidgets();
						Save();
					}
				};
				stackPanel5.Children.Add(grid3);
			}
			TextBlock componentStatus = new TextBlock
			{
				Text = "组件开关会自动保存，可随时关闭或重新开启。",
				TextWrapping = TextWrapping.Wrap,
				Foreground = System.Windows.Media.Brushes.LightGray,
				Margin = new Thickness(0.0, 6.0, 0.0, 10.0)
			};
			stackPanel5.Children.Add(componentStatus);
			stateTimer.Tick += delegate
			{
				componentStatus.Text = (string.IsNullOrEmpty(WidgetStatus) ? "组件开关会自动保存，可随时关闭或重新开启。" : WidgetStatus);
				routeSync = true;
				try
				{
					foreach (KeyValuePair<string, ToggleButton> item2 in widgetChecks)
					{
						bool flag2 = WidgetRules.Enabled(Prefs.Widgets, item2.Key);
						if (item2.Value.IsChecked != flag2)
						{
							item2.Value.IsChecked = flag2;
						}
					}
				}
				finally
				{
					routeSync = false;
				}
			};
			stackPanel5.Children.Add(FeatureUi.Button("开启全部组件", delegate
			{
				RecoverWidgets();
			}));
			stackPanel5.Children.Add(FeatureUi.Button("重新打开已启用组件", delegate
			{
				ReopenWidgets();
			}));
			func3(stackPanel5, "组件始终置顶", Prefs.Widgets.Topmost, delegate(bool value)
			{
				Prefs.Widgets.Topmost = value;
				RefreshWidgets();
			});
			func3(stackPanel5, "锁定组件位置", Prefs.Widgets.Locked, delegate(bool value)
			{
				Prefs.Widgets.Locked = value;
				RefreshWidgets();
			});
			stackPanel5.Children.Add(new TextBlock
			{
				Text = "天气城市（启用天气后联网查询）",
				Margin = new Thickness(0.0, 14.0, 0.0, 8.0)
			});
			System.Windows.Controls.TextBox city = new System.Windows.Controls.TextBox
			{
				Text = Prefs.Widgets.City,
				MinHeight = 42.0,
				TextWrapping = TextWrapping.Wrap,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				VerticalContentAlignment = VerticalAlignment.Center
			};
			stackPanel5.Children.Add(city);
			stackPanel5.Children.Add(FeatureUi.Button("应用天气城市", delegate
			{
				Prefs.Widgets.City = city.Text.Trim();
				Prefs.Widgets.Normalize();
				RefreshWidgets();
				Save();
			}));
			func2(stackPanel5, "组件背景透明度（0% 全透明）", 0.0, 100.0, Prefs.Widgets.OpacityPercent, 5.0, "%", delegate(double value)
			{
				Prefs.Widgets.OpacityPercent = value;
				foreach (WidgetPanelPosition panel in Prefs.Widgets.Panels)
				{
					panel.OpacityPercent = null;
				}
				RefreshWidgets();
				Save();
			});
			func2(stackPanel5, "本地音乐音量", 0.0, 1.0, Prefs.Widgets.Volume, 0.05, "", delegate(double value)
			{
				Prefs.Widgets.Volume = value;
				RefreshWidgets();
			});
			stackPanel5.Children.Add(new TextBlock
			{
				Text = "音乐支持封面、系统媒体会话、持久播放列表、随机与循环。备忘录自动保存并提供文本导出；切换布局或外观方案保留文字。",
				TextWrapping = TextWrapping.Wrap,
				Foreground = System.Windows.Media.Brushes.LightGray,
				Margin = new Thickness(0.0, 12.0, 0.0, 12.0)
			});
			stackPanel5.Children.Add(FeatureUi.Button("重置全部组件位置", delegate
			{
				Prefs.Widgets.Panels.Clear();
				Prefs.Placement.WidgetLeft = (Prefs.Placement.WidgetTop = null);
				RefreshWidgets();
				Save();
			}));
			bool value3 = false;
			try
			{
				value3 = StartupService.Enabled;
			}
			catch (Exception ex)
			{
				Store.Log(ex);
			}
			System.Windows.Controls.CheckBox startupCheck = new System.Windows.Controls.CheckBox
			{
				Content = "登录 Windows 后自动启动 GlassBoard",
				IsChecked = value3,
				Foreground = System.Windows.Media.Brushes.White,
				Margin = new Thickness(0.0, 10.0, 0.0, 12.0)
			};
			stackPanel6.Children.Add(startupCheck);
			TextBlock startupInfo = new TextBlock
			{
				Foreground = System.Windows.Media.Brushes.LightGray,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
			};
			stackPanel6.Children.Add(startupInfo);
			Action startupState = delegate
			{
				try
				{
					startupInfo.Text = (StartupService.Enabled ? "当前已开启。自动启动使用本程序所在路径；移动程序后，请关闭再开启以更新路径。" : "当前未开启。默认关闭，仅在你打开此开关后启用。");
				}
				catch (Exception ex3)
				{
					startupInfo.Text = "无法读取开机启动设置：" + ex3.Message;
				}
			};
			bool startupSync = false;
			Action<bool> setStartup = delegate(bool value)
			{
				if (startupSync)
				{
					return;
				}
				try
				{
					StartupService.Set(value);
					startupState();
				}
				catch (Exception ex2)
				{
					startupSync = true;
					try
					{
						startupCheck.IsChecked = StartupService.Enabled;
					}
					catch
					{
						startupCheck.IsChecked = false;
					}
					startupSync = false;
					System.Windows.MessageBox.Show(w, ex2.Message, "无法更新开机启动");
				}
			};
			startupCheck.Checked += delegate
			{
				setStartup(obj: true);
			};
			startupCheck.Unchecked += delegate
			{
				setStartup(obj: false);
			};
			startupState();
			stackPanel6.Children.Add(new TextBlock
			{
				Text = "关闭此开关会移除本软件的启动项。便携更新请保留完整程序文件夹，双击 EXE 即可运行。",
				Foreground = System.Windows.Media.Brushes.LightGray,
				TextWrapping = TextWrapping.Wrap
			});
			stackPanel6.Children.Add(new TextBlock
			{
				Text = "完整备份与恢复",
				FontSize = 18.0,
				Margin = new Thickness(0.0, 22.0, 0.0, 10.0)
			});
			stackPanel6.Children.Add(new TextBlock
			{
				Text = "备份布局、分组、快捷键、组件、便签、外观方案与自定义图标。软件与音乐文件保留在原位置；开机启动按当前电脑设置保留。",
				TextWrapping = TextWrapping.Wrap,
				Foreground = System.Windows.Media.Brushes.LightGray
			});
			WrapPanel wrapPanel4 = new WrapPanel
			{
				Margin = new Thickness(0.0, 12.0, 0.0, 6.0)
			};
			wrapPanel4.Children.Add(FeatureUi.Button("导出完整备份", delegate
			{
				ExportFullBackup();
			}));
			wrapPanel4.Children.Add(FeatureUi.Button("恢复完整备份", delegate
			{
				w.Close();
				Dock.Dispatcher.BeginInvoke((Action)delegate
				{
					RestoreFullBackup();
				});
			}));
			stackPanel6.Children.Add(wrapPanel4);
			System.Windows.Controls.Button button5 = new System.Windows.Controls.Button
			{
				Content = "保存快捷键并完成",
				Height = 42.0,
				Background = Polish.Color("#416AA9"),
				Foreground = System.Windows.Media.Brushes.White,
				Margin = new Thickness(0.0, 16.0, 0.0, 0.0)
			};
			Grid.SetRow(button5, 2);
			grid.Children.Add(button5);
			button5.Click += delegate
			{
				List<HotkeySetting> list = (from row in controls
					select new HotkeySetting
					{
						ActionId = row.Key,
						Enabled = row.Value.Item1.IsChecked.GetValueOrDefault(),
						Modifiers = HotkeyRules.ModifierValues[row.Value.Item2.SelectedIndex],
						Key = keyChoices[(string)row.Value.Item3.SelectedItem]
					} into k
					orderby k.ActionId
					select k).ToList();
				if (!HotkeyRules.Same(Prefs.Hotkeys, list) && !Dock.ApplyHotkeys(list, out var error))
				{
					System.Windows.MessageBox.Show(w, error, "快捷键未保存");
				}
				else
				{
					Prefs.Hotkeys = list.Select((HotkeySetting k) => k.Copy()).ToList();
					Save();
					RefreshTrayMenu();
					w.Close();
				}
			};
			w.Show();
			flag = true;
		}
		finally
		{
			if (!flag)
			{
				if (settingsWindow != null)
				{
					settingsWindow.Close();
				}
				cleanup();
			}
		}
	}

	public void ShellAction(int action, bool? hidden = null)
	{
		try
		{
			string text;
			switch (action)
			{
			case 1:
			{
				bool flag = hidden ?? (!DesktopVisibility.DesiredHidden(taskbar: true));
				if (flag && TaskbarAutoHide.Enabled)
				{
					TaskbarAutoHide.Set(enabled: false);
				}
				text = DesktopVisibility.SetHidden(taskbar: true, flag);
				break;
			}
			case 2:
				text = (hidden.HasValue ? DesktopVisibility.SetHidden(taskbar: false, hidden.Value) : DesktopVisibility.ToggleIcons());
				break;
			default:
				try
				{
					TaskbarAutoHide.ShowAndClear();
				}
				catch (Exception ex)
				{
					Store.Log(ex);
				}
				DesktopVisibility.ShowAll();
				text = "已执行任务栏与桌面图标恢复";
				break;
			}
			Board.SetStatus(text);
			if (Tray != null)
			{
				Tray.ShowBalloonTip(1500, "GlassBoard", text, ToolTipIcon.Info);
			}
		}
		catch (Exception ex2)
		{
			Store.Log(ex2);
			System.Windows.MessageBox.Show("无法切换系统界面：" + ex2.Message, "GlassBoard");
		}
	}

	public void Quit()
	{
		ShuttingDown = true;
		try
		{
			DesktopVisibility.RestoreOriginal();
		}
		catch (Exception ex)
		{
			Store.Log(ex);
		}
		try
		{
			TaskbarAutoHide.Restore();
		}
		catch (Exception ex2)
		{
			Store.Log(ex2);
		}
		CloseWidgets();
		Tray.Visible = false;
		Tray.Dispose();
		Dock.ReleaseHotkey();
		System.Windows.Application.Current.Shutdown();
	}

	public BoardEditState BeginBoardEdit()
	{
		return BoardEditState.Capture(Prefs);
	}

	public void EndBoardEdit(BoardEditState before, string label)
	{
		editHistory.Remember(before, Prefs, label);
	}

	public void UndoBoard()
	{
		string text = editHistory.Undo(Prefs);
		if (text != null)
		{
			Board.FinishSelection();
			Refresh();
			Board.SetStatus("已撤销：" + text);
		}
	}

	public void RedoBoard()
	{
		string text = editHistory.Redo(Prefs);
		if (text != null)
		{
			Board.FinishSelection();
			Refresh();
			Board.SetStatus("已重做：" + text);
		}
	}

	public void TransferEntries(string[] paths, string from, string to)
	{
		BoardEditState before = BeginBoardEdit();
		int num = FeatureRules.Transfer(Prefs, paths, from, to);
		EndBoardEdit(before, (from == "全部") ? "添加到分组" : "移动分组");
		Board.FinishSelection();
		Refresh();
		Board.SetStatus("已处理 " + num + " 个入口");
	}

	public void ReorderEntry(string path, Entry target)
	{
		Entry entry = Prefs.Apps.FirstOrDefault((Entry a) => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
		if (entry != null && entry != target)
		{
			BoardEditState before = BeginBoardEdit();
			int val = Prefs.Apps.IndexOf(target);
			Prefs.Apps.Remove(entry);
			Prefs.Apps.Insert(Math.Min(val, Prefs.Apps.Count), entry);
			EndBoardEdit(before, "调整图标顺序");
			Refresh();
		}
	}

	private void FlushBackupState()
	{
		Store.Save(Prefs);
		if (widgets == null)
		{
			RefreshWidgets();
		}
		if (!widgets.Notes.Flush())
		{
			throw new IOException("备忘录尚未保存，请先处理保存错误");
		}
		NoteFiles.Write(Path.Combine(Store.Folder, "notes.json"), widgets.Notes.Book);
	}

	public void ExportFullBackup()
	{
		bool modalBusy = Board.ModalBusy;
		Board.ModalBusy = true;
		try
		{
			Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
			{
				FileName = "GlassBoard_完整备份_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".gbbackup",
				Filter = "GlassBoard 完整备份|*.gbbackup"
			};
			if (saveFileDialog.ShowDialog(OwnerWindow).GetValueOrDefault())
			{
				FlushBackupState();
				BackupFiles.Create(Store.Folder, saveFileDialog.FileName);
				System.Windows.MessageBox.Show(OwnerWindow, "布局、分组、快捷键、组件、便签、外观方案与自定义图标已备份。", "备份完成");
			}
		}
		catch (Exception ex)
		{
			Store.Log(ex);
			System.Windows.MessageBox.Show(OwnerWindow, ex.Message, "备份失败");
		}
		finally
		{
			Board.ModalBusy = modalBusy;
		}
	}

	public void RestoreFullBackup()
	{
		if (settingsWindow != null)
		{
			settingsWindow.Close();
		}
		bool modalBusy = Board.ModalBusy;
		Board.ModalBusy = true;
		try
		{
			Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
			{
				Filter = "GlassBoard 完整备份|*.gbbackup"
			};
			if (!openFileDialog.ShowDialog(OwnerWindow).GetValueOrDefault())
			{
				return;
			}
			BackupFiles.Read(openFileDialog.FileName);
			if (System.Windows.MessageBox.Show(OwnerWindow, "恢复此备份中的布局、分组、快捷键与便签？\n当前配置会先自动保存为恢复前备份。开机启动开关按当前电脑保留。", "恢复完整备份", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
			{
				FlushBackupState();
				string text = Path.Combine(Store.Folder, "backups", "恢复前_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".gbbackup");
				Directory.CreateDirectory(Path.GetDirectoryName(text));
				BackupFiles.Create(Store.Folder, text);
				List<HotkeySetting> hotkeys = Prefs.Hotkeys.Select((HotkeySetting k) => k.Copy()).ToList();
				BackupFiles.Restore(openFileDialog.FileName, Store.Folder);
				Prefs = Store.Load();
				widgets.Notes.Reload();
				string error;
				bool flag = Dock.ApplyHotkeys(Prefs.Hotkeys, out error);
				if (!flag)
				{
					Prefs.Hotkeys = hotkeys;
				}
				editHistory.Clear();
				bool isVisible = Board.IsVisible;
				Refresh();
				widgets.RefreshNotes();
				RefreshTrayMenu();
				if (isVisible)
				{
					Board.Open();
				}
				System.Windows.MessageBox.Show(OwnerWindow, "配置已恢复。恢复前备份已保存到：\n" + text + (flag ? "" : ("\n快捷键与系统冲突，已保留原快捷键：" + error)), "恢复完成");
			}
		}
		catch (Exception ex)
		{
			Store.Log(ex);
			System.Windows.MessageBox.Show(OwnerWindow, ex.Message, "恢复失败");
		}
		finally
		{
			Board.ModalBusy = modalBusy;
		}
	}

	private static BitmapSource CreatorImage(string key, bool thumbnail = false)
	{
		// 只放一个赞赏码：Miya 的 qr.jpg（嵌入资源），不再使用原作者的加密收款图片
		byte[] bytes;
		using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("qr.jpg"))
		{
			if (s == null)
			{
				throw new InvalidDataException("赞赏码资源缺失。");
			}
			using MemoryStream ms = new MemoryStream();
			s.CopyTo(ms);
			bytes = ms.ToArray();
		}
		if (thumbnail)
		{
			return ThumbnailImages.Read(bytes, 960);
		}
		using MemoryStream streamSource = new MemoryStream(bytes, writable: false);
		BitmapImage bitmapImage = new BitmapImage();
		bitmapImage.BeginInit();
		bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
		bitmapImage.StreamSource = streamSource;
		bitmapImage.EndInit();
		bitmapImage.Freeze();
		return bitmapImage;
	}

	private void ShowCreatorImage(Window owner, string key, string title)
	{
		Window window = FeatureUi.Dialog(title, owner, 680.0, 850.0);
		window.ResizeMode = ResizeMode.CanResize;
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(18.0)
		};
		TextBlock element = new TextBlock
		{
			Text = "感谢你的支持 · Miya",
			Foreground = System.Windows.Media.Brushes.White,
			FontSize = 15.0,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		DockPanel.SetDock(element, System.Windows.Controls.Dock.Bottom);
		dockPanel.Children.Add(element);
		dockPanel.Children.Add(new System.Windows.Controls.Image
		{
			Source = CreatorImage(key),
			Stretch = Stretch.Uniform
		});
		window.Content = dockPanel;
		window.ShowDialog();
	}

	public void BuildCreatorSupport(StackPanel panel, Window owner)
	{
		panel.Children.Add(new TextBlock
		{
			Text = "由 Miya 制作",
			Foreground = System.Windows.Media.Brushes.White,
			FontSize = 25.0,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 10.0, 0.0, 12.0)
		});
		panel.Children.Add(new TextBlock
		{
			Text = "谢谢你的支持与陪伴，也谢谢每一次认真反馈。你的鼓励，会帮助我继续打磨 GlassBoard，让桌面更舒服、更好用。",
			TextWrapping = TextWrapping.Wrap,
			Foreground = System.Windows.Media.Brushes.LightGray,
			FontSize = 14.0,
			LineHeight = 24.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
		});
		WrapPanel cards = new WrapPanel();
		panel.Children.Add(cards);
		Action<string, string> action = delegate(string key, string title)
		{
			StackPanel stackPanel = new StackPanel
			{
				Children = { (UIElement)new TextBlock
				{
					Text = title,
					Foreground = System.Windows.Media.Brushes.White,
					FontSize = 17.0,
					FontWeight = FontWeights.SemiBold,
					Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
				} }
			};
			System.Windows.Controls.Image image = new System.Windows.Controls.Image
			{
				Source = CreatorImage(key, thumbnail: true),
				Height = 300.0,
				Stretch = Stretch.Uniform,
				Cursor = System.Windows.Input.Cursors.Hand,
				ToolTip = "点击查看大图"
			};
			image.MouseLeftButtonUp += delegate
			{
				ShowCreatorImage(owner, key, title);
			};
			stackPanel.Children.Add(image);
			stackPanel.Children.Add(FeatureUi.Button("查看大图", delegate
			{
				ShowCreatorImage(owner, key, title);
			}));
			cards.Children.Add(new Border
			{
				Width = 238.0,
				CornerRadius = new CornerRadius(18.0),
				Padding = new Thickness(14.0),
				Margin = new Thickness(0.0, 0.0, 14.0, 14.0),
				Background = Polish.Color("#242936"),
				BorderBrush = Polish.Color("#394151"),
				BorderThickness = new Thickness(1.0),
				Child = stackPanel
			});
		};
		try
		{
			action("Miya", "赞赏码");
		}
		catch (Exception ex)
		{
			cards.Children.Clear();
			Store.Log(ex);
			panel.Children.Add(new TextBlock
			{
				Text = "收款资源校验失败，已停止显示。请重新解压原版程序。",
				TextWrapping = TextWrapping.Wrap,
				Foreground = System.Windows.Media.Brushes.LightGray,
				Margin = new Thickness(0.0, 12.0, 0.0, 12.0)
			});
		}
		panel.Children.Add(new TextBlock
		{
			Text = "随心支持，一份鼓励就很珍贵。感谢你让这份创作继续生长。",
			TextWrapping = TextWrapping.Wrap,
			Foreground = System.Windows.Media.Brushes.LightGray,
			Margin = new Thickness(0.0, 8.0, 0.0, 12.0),
			FontSize = 13.0
		});
	}
}
