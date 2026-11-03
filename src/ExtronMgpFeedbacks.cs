using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core.Logging;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>
	/// Bridge feedbacks driven by an <see cref="ExtronMgpModel"/>, and the join linking shared by the real device and the mock.
	/// </summary>
	public class ExtronMgpFeedbacks
	{
		private readonly ExtronMgpModel model;

		/// <summary>Current layout as an MGP window preset number (0 = unknown)</summary>
		public IntFeedback LayoutPreset { get; private set; }

		/// <summary>Per-window input preset number (0 = unknown), index 0-3 for windows 1-4</summary>
		public IntFeedback[] WindowInputPreset { get; private set; }

		/// <summary>Per-window mute, index 0-3</summary>
		public BoolFeedback[] WindowMute { get; private set; }

		/// <summary>Per-window freeze, index 0-3</summary>
		public BoolFeedback[] WindowFreeze { get; private set; }

		/// <summary>Constructor; fires feedbacks whenever the model changes</summary>
		public ExtronMgpFeedbacks(ExtronMgpModel model)
		{
			this.model = model;
			LayoutPreset = new IntFeedback("layoutPreset", () => model.CurrentLayout);
			WindowInputPreset = new IntFeedback[ExtronMgpProtocol.WindowCount];
			WindowMute = new BoolFeedback[ExtronMgpProtocol.WindowCount];
			WindowFreeze = new BoolFeedback[ExtronMgpProtocol.WindowCount];
			for (var i = 0; i < ExtronMgpProtocol.WindowCount; i++)
			{
				var w = i + 1;
				WindowInputPreset[i] = new IntFeedback("window" + w + "InputPreset", () => model.GetInputPreset(w));
				WindowMute[i] = new BoolFeedback("window" + w + "Mute", () => model.GetMute(w));
				WindowFreeze[i] = new BoolFeedback("window" + w + "Freeze", () => model.GetFreeze(w));
			}

			model.StateChanged += (o, a) => FireAll();
		}

		/// <summary>Fires every state feedback</summary>
		public void FireAll()
		{
			LayoutPreset.FireUpdate();
			foreach (var f in WindowInputPreset) f.FireUpdate();
			foreach (var f in WindowMute) f.FireUpdate();
			foreach (var f in WindowFreeze) f.FireUpdate();
		}

		/// <summary>
		/// Links layout, window input, mute and freeze joins.  Online/connect/status/name/IP joins are linked by the caller.
		/// </summary>
		public void Link(IExtronMgp device, BasicTriList trilist, ExtronMgpJoinMap joinMap)
		{
			trilist.SetUShortSigAction(joinMap.LayoutSelect.JoinNumber, v => device.RecallLayoutPreset(v));
			LayoutPreset.LinkInputSig(trilist.UShortInput[joinMap.LayoutSelect.JoinNumber]);

			for (var i = 0; i < ExtronMgpProtocol.WindowCount; i++)
			{
				var w = i + 1;
				var offset = (uint)i;

				trilist.SetUShortSigAction(joinMap.WindowInputSelect.JoinNumber + offset, v => device.SelectWindowInputPreset(w, v));
				WindowInputPreset[i].LinkInputSig(trilist.UShortInput[joinMap.WindowInputSelect.JoinNumber + offset]);

				trilist.SetSigTrueAction(joinMap.WindowMuteOn.JoinNumber + offset, () => device.SetWindowMute(w, true));
				trilist.SetSigTrueAction(joinMap.WindowMuteOff.JoinNumber + offset, () => device.SetWindowMute(w, false));
				WindowMute[i].LinkInputSig(trilist.BooleanInput[joinMap.WindowMuteOn.JoinNumber + offset]);
				WindowMute[i].LinkComplementInputSig(trilist.BooleanInput[joinMap.WindowMuteOff.JoinNumber + offset]);

				trilist.SetSigTrueAction(joinMap.WindowFreezeOn.JoinNumber + offset, () => device.SetWindowFreeze(w, true));
				trilist.SetSigTrueAction(joinMap.WindowFreezeOff.JoinNumber + offset, () => device.SetWindowFreeze(w, false));
				WindowFreeze[i].LinkInputSig(trilist.BooleanInput[joinMap.WindowFreezeOn.JoinNumber + offset]);
				WindowFreeze[i].LinkComplementInputSig(trilist.BooleanInput[joinMap.WindowFreezeOff.JoinNumber + offset]);
			}

			FireAll();
		}
	}
}
