# EcoInSpace
Mods for the game Eco (for 0.9.5.1)
These are two mods I'm working on that will bring outer space just that bit closer

1. Mars Terraforming: Turn existing terrain into a martian landscape
  -Commands:
  /makemars x,y,w,h
    x,y of lower left coord
    width, height of land or sea to terraform
    admin only

2. Oxygen: Simulate need for oxygen when on Mars, through (invisible) oxygen tanks for each player. It uses no new items, so ought to be safe to uninstall if it doesn't work (theory, but not yet tested). The capacity to store oxygen is governed by the type of backpack the player is wearing. Top up by holding empty barrels and opening the UI of a waste filter that is connected to a power source. Each barrel fills up 5 minutes of oxygen time. You can also breath freely when in a T3+ room that contains a powered waste filter and has no holes
  -Commands:
  /ox
    Tells the player how much oxygen is left in their tank
  /atm
    Tells the player what the room still needs in order to provide free oxygen
