using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using PepperDash.Core;
using PepperDash.Essentials.AppServer.Messengers;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.DeviceTypeInterfaces;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>
	/// The standard "screens with layouts" view of an MGP (<see cref="IHasScreensWithLayouts"/>), shared by the
	/// real device and the mock.
	/// </summary>
	/// <remarks>
	/// The MGP drives one screen, id <see cref="ScreenId"/>. Its layouts are the configured layouts; a layout's
	/// key and index are its MGP preset slot. Applying a layout recalls that preset.
	/// </remarks>
	public class ExtronMgpScreens
	{
		/// <summary>The MGP has a single output, so a single screen</summary>
		public const uint ScreenId = 1;

		private readonly IExtronMgp mgp;
		private readonly string screenName;

		/// <summary>Selectable layouts of the screen, registered as device "{key}-screen-1"</summary>
		public ExtronMgpLayoutSelector Layouts { get; private set; }

		/// <summary>Key of <see cref="Layouts"/> and the messenger path suffix</summary>
		public string LayoutsKey { get { return mgp.Key + "-screen-" + ScreenId; } }

		/// <summary>
		/// Constructor
		/// </summary>
		public ExtronMgpScreens(IExtronMgp mgp, string screenName)
		{
			this.mgp = mgp;
			this.screenName = screenName;
			Layouts = new ExtronMgpLayoutSelector(LayoutsKey, mgp);
		}

		/// <summary>The screens, built from the model's current layout names</summary>
		public Dictionary<uint, ScreenInfo> Screens
		{
			get
			{
				return new Dictionary<uint, ScreenInfo>
				{
					{
						ScreenId,
						new ScreenInfo
						{
							Enabled = true,
							Name = screenName,
							ScreenIndex = (int)ScreenId,
							Layouts = mgp.Model.Layouts.ToDictionary(l => (uint)l.Preset, ToLayoutInfo)
						}
					}
				};
			}
		}

		/// <summary>Applies the layout whose key is the MGP preset slot</summary>
		public void ApplyLayout(uint screenId, uint layoutIndex)
		{
			if (screenId != ScreenId)
			{
				Debug.LogError(mgp, "[ApplyLayout] Screen '" + screenId + "' not found.");
				return;
			}

			if (!mgp.Model.Layouts.Any(l => l.Preset == layoutIndex))
			{
				Debug.LogError(mgp, "[ApplyLayout] Layout '" + layoutIndex + "' not found for screen '" + screenId + "'.");
				return;
			}

			mgp.RecallLayoutPreset((int)layoutIndex);
		}

		/// <summary>Registers the layout selector with the device manager and both messengers with Mobile Control</summary>
		public void RegisterMessengers(IMobileControl controller, IHasScreensWithLayouts owner)
		{
			DeviceManager.AddDevice(Layouts);

			controller.AddDeviceMessenger(new IHasScreensWithLayoutsMessenger(mgp.Key + "-screens", "/device/" + mgp.Key, owner));
			controller.AddDeviceMessenger(new ISelectableItemsMessenger<uint>(LayoutsKey + "-" + controller.Key, "/device/" + LayoutsKey, Layouts, "screen-" + ScreenId));
		}

		private static LayoutInfo ToLayoutInfo(ExtronMgpPresetItem layout)
		{
			return new LayoutInfo
			{
				LayoutName = layout.Name,
				LayoutIndex = layout.Preset,
				LayoutType = layout.LayoutType,
				Windows = WindowsFor(layout.LayoutType)
			};
		}

		private static Dictionary<uint, WindowConfig> WindowsFor(string layoutType)
		{
			var count = 0;
			switch ((layoutType ?? string.Empty).ToLowerInvariant())
			{
				case "single": count = 1; break;
				case "double": count = 2; break;
				case "triple": count = 3; break;
				case "quad": count = 4; break;
			}

			return Enumerable.Range(1, count).ToDictionary(w => (uint)w, w => new WindowConfig { Label = "Window " + w });
		}
	}

	/// <summary>
	/// The selectable layouts of the MGP's screen: choosing one applies it, and the current one follows the unit.
	/// </summary>
	public class ExtronMgpLayoutSelector : ISelectableItems<uint>, IKeyName
	{
		private readonly IExtronMgp mgp;
		private Dictionary<uint, ISelectableItem> items = new Dictionary<uint, ISelectableItem>();
		private uint currentItem;

		/// <inheritdoc />
		public string Key { get; private set; }

		/// <inheritdoc />
		public string Name { get { return Key; } }

		/// <inheritdoc />
		public event EventHandler ItemsUpdated;

		/// <inheritdoc />
		public event EventHandler CurrentItemChanged;

		/// <inheritdoc />
		public Dictionary<uint, ISelectableItem> Items
		{
			get { return items; }
			set
			{
				items = value;
				var handler = ItemsUpdated;
				if (handler != null) handler(this, EventArgs.Empty);
			}
		}

		/// <inheritdoc />
		public uint CurrentItem
		{
			get { return currentItem; }
			set
			{
				currentItem = value;
				foreach (var item in items.Values.OfType<LayoutItem>()) item.IsSelected = item.Preset == value;
				var handler = CurrentItemChanged;
				if (handler != null) handler(this, EventArgs.Empty);
			}
		}

		/// <summary>
		/// Constructor
		/// </summary>
		public ExtronMgpLayoutSelector(string key, IExtronMgp mgp)
		{
			Key = key;
			this.mgp = mgp;

			RebuildItems();
			CurrentItem = (uint)mgp.Model.CurrentLayout;

			mgp.Model.StateChanged += (o, a) =>
			{
				var current = (uint)mgp.Model.CurrentLayout;
				if (current != currentItem) CurrentItem = current;
			};
			mgp.Model.InfoChanged += (o, a) => RebuildItems();
		}

		private void RebuildItems()
		{
			var rebuilt = new Dictionary<uint, ISelectableItem>();
			foreach (var layout in mgp.Model.Layouts)
			{
				rebuilt[(uint)layout.Preset] = new LayoutItem(layout.Preset, layout.Name, mgp) { IsSelected = layout.Preset == currentItem };
			}
			Items = rebuilt;
		}

		private class LayoutItem : ISelectableItem
		{
			private readonly IExtronMgp mgp;
			private bool isSelected;

			public int Preset { get; private set; }
			public string Key { get; private set; }
			public string Name { get; private set; }
			public event EventHandler ItemUpdated;

			public bool IsSelected
			{
				get { return isSelected; }
				set
				{
					if (isSelected == value) return;
					isSelected = value;
					var handler = ItemUpdated;
					if (handler != null) handler(this, EventArgs.Empty);
				}
			}

			public LayoutItem(int preset, string name, IExtronMgp mgp)
			{
				Preset = preset;
				Key = preset.ToString();
				Name = name;
				this.mgp = mgp;
			}

			public void Select()
			{
				mgp.RecallLayoutPreset(Preset);
			}
		}
	}

	/// <summary>
	/// Mobile Control messenger for <see cref="IHasScreensWithLayouts"/>: "/fullStatus" posts the screens and their
	/// layouts. Same shape as the one in PepperDash.Essentials.DM and epi-analogWay-aquilon; the layout selection
	/// itself goes through the core selectable-items messenger.
	/// </summary>
	public class IHasScreensWithLayoutsMessenger : MessengerBase
	{
		private readonly IHasScreensWithLayouts hasScreensWithLayouts;

		/// <summary>
		/// Constructor
		/// </summary>
		public IHasScreensWithLayoutsMessenger(string key, string messagePath, IHasScreensWithLayouts hasScreensWithLayouts)
			: base(key, messagePath, hasScreensWithLayouts as IKeyName)
		{
			this.hasScreensWithLayouts = hasScreensWithLayouts;
		}

		/// <inheritdoc />
		protected override void RegisterActions()
		{
			base.RegisterActions();

			AddAction("/fullStatus", (id, context) => SendFullStatus());
		}

		private void SendFullStatus()
		{
			PostStatusMessage(new IHasScreensWithLayoutsStateMessage { Screens = hasScreensWithLayouts.Screens });
		}
	}

	/// <summary>State posted by <see cref="IHasScreensWithLayoutsMessenger"/></summary>
	public class IHasScreensWithLayoutsStateMessage : DeviceStateMessageBase
	{
		/// <summary>Screens keyed by screen id</summary>
		[JsonProperty("screens")]
		public Dictionary<uint, ScreenInfo> Screens { get; set; }
	}
}
