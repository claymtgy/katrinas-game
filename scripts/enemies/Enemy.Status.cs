using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Effects players put on enemies with the level-12 abilities (GDD §6): the Archer's Mark
/// (everyone hits it 25% harder; a marker shows through walls) and the Rogue's Venom poison
/// (4 damage a second for 5 s, up to 3 stacks).</summary>
public abstract partial class Enemy
{
    /// <summary>Marked until then (this machine's clock).</summary>
    public double MarkedUntil { get; private set; }
    public bool Marked => Net.Now < MarkedUntil && Current != State.Dead;

    private readonly List<(double Until, PlayerController From)> _poison = new();
    private float _poisonTick;
    private Label3D _markLabel;

    /// <summary>Server: poison stacks working now.</summary>
    public int PoisonStacks
    {
        get
        {
            _poison.RemoveAll(s => s.Until <= Net.Now);
            return _poison.Count;
        }
    }

    /// <summary>Server: one more stack of Venom poison (the oldest is refreshed past three).</summary>
    public void Poison(PlayerController from)
    {
        _poison.RemoveAll(s => s.Until <= Net.Now);
        if (_poison.Count >= Net.VenomMaxStacks) _poison.RemoveAt(0);
        _poison.Add((Net.Now + Net.VenomPoisonSeconds, from));
    }

    /// <summary>Server, every physics frame: poison ticks once a second, through armour.</summary>
    private void TickPoison(float dt)
    {
        if (_poison.Count == 0) { _poisonTick = 0; return; }
        int stacks = PoisonStacks;
        if (stacks == 0) return;
        _poisonTick += dt;
        if (_poisonTick < 1f) return;
        _poisonTick -= 1f;
        var from = _poison[^1].From;
        Net.PoisonTicking = true;
        Health.TakeDamage(Net.VenomDps * stacks, IsInstanceValid(from) ? from : null, true);
        Net.PoisonTicking = false;
    }

    /// <summary>Every machine: marked until then; the marker shows over it through walls.</summary>
    public void SetMarked(double until)
    {
        MarkedUntil = until;
        if (Net.Instance.IsDedicated) return;
        if (_markLabel == null)
        {
            _markLabel = new Label3D
            {
                Name = "MarkLabel", Text = "◆", FontSize = 96, OutlineSize = 18, PixelSize = 0.005f,
                Modulate = new Color(1f, 0.3f, 0.25f), OutlineModulate = new Color(0.2f, 0f, 0f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FixedSize = false,
                Position = Vector3.Up * 2.6f,
            };
            AddChild(_markLabel);
        }
        _markLabel.Visible = Marked;
    }

    private void TickStatus(float dt)
    {
        if (Net.Instance.IsServer && Current != State.Dead) TickPoison(dt);
        if (_markLabel != null && _markLabel.Visible != Marked) _markLabel.Visible = Marked;
    }
}
