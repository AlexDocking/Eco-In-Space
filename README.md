# EcoInSpace
Mods for the game Eco (for 0.9.5.1)
These are two mods I'm working on that will bring outer space just that bit closer

1. Mars Terraforming
Turn existing terrain into a martian landscape
  -Commands:
  /makemars x,y,w,h
    x,y of lower left coord
    width, height of land or sea to terraform
    admin only

2. Oxygen
Simulate need for oxygen when on Mars, through (invisible) oxygen tanks for each player.
It uses no new items, so ought to be safe to uninstall if it doesn't work (in theory, but not yet tested).
The capacity to store oxygen is governed by the type of backpack the player is wearing.
Top up by holding empty barrels and opening the UI of a waste filter that is connected to a power source.
Each barrel fills up 5 minutes of oxygen time.
You can also breath freely when in a T3+ room that contains a powered waste filter and has no holes
  -Commands:
  /ox
    Tells the player how much oxygen is left in their tank
  /atm
    Tells the player what the room still needs in order to provide free oxygen

Known issues:
1. Plants don't get destroyed when terraforming, including water plants
2. Animals don't despawn after terraforming
3. Trying to change animal habitability/capacity layers doesn't work
4. There are thin columns of gneiss, which when turned to water can lead to deep narrow holes into the ground. Solution may be to make gneiss above a certain height or too close to the surface into sandstone, and only turn deep gneiss depsosits to water
5. No way yet of linking waste filter rooms to other nearby pressurised rooms that don't have their own waste filters. Currently every room needs a waste filter in order to breath without using the oxygen tank
6. Waste filters don't add any load to the power grid
