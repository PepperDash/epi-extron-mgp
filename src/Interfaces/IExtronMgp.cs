using PepperDash.Core;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>
	/// Control surface shared by the real <see cref="ExtronMgpDevice"/> and <see cref="ExtronMgpMock"/>.
	/// The messenger and bridge are written against this, so mock and real devices behave identically upstream.
	/// </summary>
	public interface IExtronMgp : IKeyName
	{
		/// <summary>Hardware-independent state</summary>
		ExtronMgpModel Model { get; }

		/// <summary>Feedbacks for the bridge</summary>
		ExtronMgpFeedbacks Feedbacks { get; }

		/// <summary>Recall the layout configured under <paramref name="layoutId"/></summary>
		void RecallLayout(string layoutId);

		/// <summary>Recall window preset <paramref name="preset"/> (1-128)</summary>
		void RecallLayoutPreset(int preset);

		/// <summary>Select the input configured under <paramref name="inputId"/> for <paramref name="window"/> (1-4)</summary>
		void SelectWindowInput(int window, string inputId);

		/// <summary>Recall input preset <paramref name="preset"/> (1-128) to <paramref name="window"/> (1-4)</summary>
		void SelectWindowInputPreset(int window, int preset);

		/// <summary>Mute or unmute a window</summary>
		void SetWindowMute(int window, bool mute);

		/// <summary>Freeze or unfreeze a window</summary>
		void SetWindowFreeze(int window, bool freeze);
	}
}
