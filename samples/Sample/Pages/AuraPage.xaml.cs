using Shiny.Maui.AiConversation;
using Shiny.Maui.Controls.FloatingPanel;

namespace Sample.Pages;

public partial class AuraPage : ShinyContentPage
{
    bool isAnimating;

    public AuraPage()
    {
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is AuraViewModel vm)
        {
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AuraViewModel.CurrentState))
                    AnimateForState(vm.CurrentState);
            };
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is AuraViewModel vm)
            AnimateForState(vm.CurrentState);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        this.AbortAnimation("AuraPulse");
        this.AbortAnimation("AuraSpin");
        this.AbortAnimation("AuraRipple");
        isAnimating = false;
    }

    void AnimateForState(AiState state)
    {
        this.AbortAnimation("AuraPulse");
        this.AbortAnimation("AuraSpin");
        this.AbortAnimation("AuraRipple");
        isAnimating = false;

        switch (state)
        {
            case AiState.Idle:
                AnimateIdle();
                break;
            case AiState.Listening:
                AnimateListening();
                break;
            case AiState.Thinking:
                AnimateThinking();
                break;
            case AiState.Responding:
                AnimateResponding();
                break;
        }
    }

    void AnimateIdle()
    {
        isAnimating = true;
        var animation = new Animation(v =>
        {
            OuterRing.Scale = 0.9 + (v * 0.1);
            OuterRing.Opacity = 0.2 + (v * 0.15);
            InnerOrb.Scale = 0.85 + (v * 0.15);
            InnerOrb.Opacity = 0.3 + (v * 0.2);
        }, 0, 1);

        animation.Commit(this, "AuraPulse", length: 3000, easing: Easing.SinInOut,
            finished: (_, _) => { if (isAnimating) AnimateIdle(); });
    }

    void AnimateListening()
    {
        isAnimating = true;
        RippleRing.IsVisible = true;
        RippleRing.Scale = 0.5;
        RippleRing.Opacity = 0.8;

        var animation = new Animation(v =>
        {
            OuterRing.Scale = 0.95 + (v * 0.15);
            OuterRing.Opacity = 0.5 + (v * 0.3);
            InnerOrb.Scale = 0.9 + (v * 0.2);
            InnerOrb.Opacity = 0.6 + (v * 0.3);
            RippleRing.Scale = 0.6 + (v * 0.8);
            RippleRing.Opacity = 0.8 - (v * 0.8);
        }, 0, 1);

        animation.Commit(this, "AuraRipple", length: 1500, easing: Easing.CubicOut,
            finished: (_, _) =>
            {
                if (isAnimating) AnimateListening();
                else RippleRing.IsVisible = false;
            });
    }

    void AnimateThinking()
    {
        isAnimating = true;
        var animation = new Animation(v =>
        {
            OuterRing.Rotation = v * 360;
            OuterRing.Opacity = 0.5 + (Math.Sin(v * Math.PI * 2) * 0.3);
            InnerOrb.Scale = 0.8 + (Math.Sin(v * Math.PI * 4) * 0.15);
            InnerOrb.Opacity = 0.5 + (Math.Sin(v * Math.PI * 3) * 0.3);
        }, 0, 1);

        animation.Commit(this, "AuraSpin", length: 2000, easing: Easing.Linear,
            finished: (_, _) => { if (isAnimating) AnimateThinking(); });
    }

    void AnimateResponding()
    {
        isAnimating = true;
        var animation = new Animation(v =>
        {
            OuterRing.Scale = 1.0 + (v * 0.2);
            OuterRing.Opacity = 0.6 + (v * 0.4);
            InnerOrb.Scale = 0.9 + (Math.Sin(v * Math.PI * 2) * 0.2);
            InnerOrb.Opacity = 0.7 + (v * 0.3);
        }, 0, 1);

        animation.Commit(this, "AuraPulse", length: 1000, easing: Easing.SinInOut,
            finished: (_, _) => { if (isAnimating) AnimateResponding(); });
    }
}
