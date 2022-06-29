namespace EcoInSpace
{
    using Eco.Gameplay.Players;

    public interface ILogInOutEventSubscriber
    {
        void OnUserLoggedIn(User user);

        void OnUserLoggedOut(User user);
    }
}