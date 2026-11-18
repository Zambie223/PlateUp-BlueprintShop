using System.Collections.Generic;
using Controllers;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlueprintShop
{
    /// <summary>
    /// Keeps swallowing a player's input after the shop closes, until every button is released.
    ///
    /// Why: the click (or Esc/Back press) that closes the shop is still "Pressed" in that frame,
    /// and Unity doesn't fix whether ShopView.Update or the game's input update
    /// (GameCreator.Update -> InputSource.Update) runs first. If the shop closes first, the
    /// press reaches the game. In build mode that makes the crane grab whatever is under it.
    /// The same applies to a press still held in the following frames.
    ///
    /// The drain is registered last, so InputSource offers it the input first. It returns
    /// Consumed (so the ECS gets neutral input) and removes itself on the first frame with no
    /// button down. Movement isn't checked, so a held movement key never keeps it alive.
    /// </summary>
    internal sealed class InputDrain : IInputConsumer
    {
        /// <summary>Safety net: never block a player's buttons for longer than this.</summary>
        private const float MaxSeconds = 5f;

        private static readonly Dictionary<int, InputDrain> Active = new Dictionary<int, InputDrain>();

        private readonly int PlayerId;
        private readonly bool CheckMouse;
        private readonly float StartedAt;

        private InputDrain(int playerId, bool checkMouse)
        {
            PlayerId = playerId;
            CheckMouse = checkMouse;
            StartedAt = Time.unscaledTime;
        }

        public static void Begin(int playerId, bool checkMouse)
        {
            if (Active.TryGetValue(playerId, out InputDrain old))
            {
                LocalInputSourceConsumers.Remove(old);
            }
            var drain = new InputDrain(playerId, checkMouse);
            Active[playerId] = drain;
            LocalInputSourceConsumers.Register(drain);
        }

        public InputConsumerState TakeInput(int player_id, InputState state)
        {
            if (player_id != PlayerId)
            {
                return InputConsumerState.NotConsumed;
            }
            if (AnyButtonDown(state) && Time.unscaledTime - StartedAt < MaxSeconds)
            {
                return InputConsumerState.Consumed;
            }
            End();
            return InputConsumerState.NotConsumed;
        }

        private void End()
        {
            LocalInputSourceConsumers.Remove(this);
            if (Active.TryGetValue(PlayerId, out InputDrain current) && current == this)
            {
                Active.Remove(PlayerId);
            }
        }

        private bool AnyButtonDown(InputState s)
        {
            if (Down(s.InteractAction) || Down(s.GrabAction) || Down(s.SecondaryAction1) || Down(s.SecondaryAction2)
                || Down(s.StopMoving) || Down(s.MenuTrigger) || Down(s.MenuSelect) || Down(s.MenuCancel)
                || Down(s.MenuUp) || Down(s.MenuDown) || Down(s.MenuLeft) || Down(s.MenuRight))
            {
                return true;
            }
            // The shop's own clicks are read from the raw mouse; cover them even if the player's
            // bindings don't map these buttons to an action.
            Mouse mouse = CheckMouse ? Mouse.current : null;
            return mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed);
        }

        private static bool Down(ButtonState b) => b == ButtonState.Pressed || b == ButtonState.Held;
    }
}
