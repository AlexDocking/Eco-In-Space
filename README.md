# Eco In Space
Mods for Eco 9.5
These are two mods I'm working on that will bring outer space just that bit closer

# Dependencies
EM Framework (v3.2.1) (for serialization)

# Installation
Drop the 'Eco In Space' folder into Mods/UserCode

# 1. Mars Terraforming
Turn existing terrain into a martian landscape.
It will automatically add the requirement for oxygen to this area. Only virgin terrain should be terraformed; it is expected that servers would want to terraform before the start of a season. Using on land with placed objects may result in unexpected behaviour. Note that the process may take a while to complete if the area is large. 
  -Admin Commands:
  /makemars x,y,w,h
    x,y of lower left coord.
    width, height of area to terraform.

# 2. Oxygen
Simulate need for oxygen when on Mars through personal oxygen tanks for each player, and by connecting waste filters to rooms.
Most of how the mod works should be straightforward to understand: players need oxygen when they are outside on Mars, and rooms must be pressurised and sufficiently filtered to be breathable.
It uses no new items, so it is safe to uninstall if necessary.

# 2.1 Oxygen Backpacks
Every backpack comes with an integrated and weightless* oxygen tank that allows players to breathe when not on Earth.
The number of litres of stored oxygen a player can carry around with them is governed by the type of backpack the player is wearing.
Players use up their personal oxygen if they're not able to breathe the air in the room or if they're outside. For reference, Basic Backpacks can hold 10 minutes of oxygen while the largest, Bearpacks, can hold 60 minutes.
If a player runs out of oxygen they will take damage at a rate of 50 calories per second.
Top up by holding empty barrels and opening the UI of a waste filter that is connected to a power source.
Each barrel fills up 5 minutes of oxygen time and consumes the barrel.
Changing backpacks empties all the oxygen**.

*Implementation-wise it is simply a number stored with each player and does not change backpacks in any way.
**Due to the way Eco handles backpacks, there is no way to identify indidual backpacks as they are not unique objects. This means there is no way of knowing whether a backpack has been previously worn or not, making saving leftover oxygen impossible.

  -User Commands:
  /ox
    Tells the player how much oxygen is left in their tank.

# 2.2 Oxygen Rooms
(Note: I use oxygen supply and filtering interchangeably) Players can also breath freely when in a valid room system that either contains or is connected to a sufficient number of waste filters. You won't use up any backpack oxygen if you can breathe the room air.
Rooms connected to each other without doors count as one continuous space, and oxygen is provided (or not) based on all the rooms as a collective. The result of this is that not every room needs its own direct connection to a waste filter.

# Room Requirements for oxygen:
1. Is a valid Eco room i.e. windows and doors are of a certain size, and the room size is not too large. Invalid Eco rooms are considered outside
2. Where there are windows or doorways without door objects, they must shared with other rooms.
3. If any of the rooms in a connected room system have windows to the outside, all of the rooms in the connected room system are depressurised
4. No narrow hallways connected without doors. Eco doesn't recognise these as rooms and therefore the mod considers them outside, depressurising the room
5. The room system must be connected by waste filters. These can be directly in the room or connected via pipes from the filter's output connection
6. Where waste filters have open ends to the outside or depressurised rooms, no oxygen will escape from those and the filter's capacity will only be sent to the pressurised rooms it is connected to.
7. The room airtightness tier must be high enough to retain enough oxygen to supply  the whole room. Low airtightness tier results in lost oxygen and rooms are only breathable if the whole of the room's volume is filtered. For example a 1500 m^3 room that has 2000 m^3 of oxygen piped to it and leaks 50% of the air will only have 1000 m^3 of the 1500 needed to let players breathe. The solution is to use more airtight materials or connect to more filters

  -User Commands:
  /atm
    Tells the player what the room/rooms still needs in order to provide oxygen.
  /tierreport
    Tells the player what the airtightness of the room materials in the current room are. They are in many cases different to the material tier that Eco uses. It means that some materials will leak more air than others, and only rooms that receive enough oxygen for their total volume will allow players to breathe.
  /atmreport
    Gives a detailed report on the oxygen status of the current room, including connected rooms, filters, airtightness tier and locations of open windows.

# Disclaimer:
I have only been able to test on a small world with one account, so you may find issues on your world that I haven't come across.
While I have intentionally avoided adding or changing anything in the game which I know to be serialized into the save file, I cannot 100% guarantee it will be safe for your server file.

# Known issues:
1. Animals don't despawn after terraforming.
2. Trying to change animal habitability/capacity layers doesn't work properly, so there will be unwanted animals spawning until I find a solution.
3. No walls to separate Mars from Earth.
3. Waste filters don't need to be part of the power grid to provide oxygen to rooms, as the game will not let me access the power grid information for them outside of player interactions with them, which is why refilling personal oxygen can have the power requirement but not for rooms.
4. Shared walls between rooms count towards the airtightness tier twice.
5. The atmreport doesn't say where the pipe outputs are which feed the room.
6. If you rollback your server the oxygen level of each user and the location of oxygen zones will still be from the future.