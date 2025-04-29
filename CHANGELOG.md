# changelog

## [1.1.6] (2025-04-28)

* bumped repolib version, it got updated 2 hours before I pushed 1.1.5
* ¯\_(ツ)_/¯

## [1.1.5] (2025-04-28)

* offloaded chat messages to a separate resource file
* adjusted message speed for all heal outcomes as I had misinterpreted the speed parameter and set them way too slow

## [1.1.3] (2025-04-17)

* chat speed adjustments
* couple other minor tweaks
* should be done for now

## [1.1.2] (2025-04-13)

* fixed a couple incorrect array refs (would have resulted in not all possible dialogue options being rolled)

## [1.1.1] (2025-04-13)

* changed super-rare chat from hardcoded single message to random selection

## [1.1.0] (2025-04-13)

* the recipient of a sacrificial heal will now say a forced message in chat to indicate the outcome
* visual indicators are applied for non-common outcomes as well
* odds adjusted:

> * common event: 69% chance
> * uncommon event: 20% chance
> * rare event: 10% chance
> * very rare event: **1% chance**

## [1.0.7] (2025-04-11)

* fixed bug with hideLerp by switching patch from prefix to postfix
* tidied up some other stuff

## [1.0.5] (2025-04-08)

* removed logging on timer iteration as it was too noisy
* made logging more consistent with my other mod
* fixed bepinex version

## [1.0.4] (2025-04-06)

* fixed some very broken grabbingTimer logic (sorry)
* added some additional logging
* changed namespace from beta name to final name

## [1.0.2] (2025-04-05)

* adjusted incorrect odds
* re-arranged RNG outcomes to be more logical

## [1.0.1] (2025-04-05)

* added an additional possible RNG outcome which results in a perm boost to a random stat for the recipient
* fixed an incorrect heal amount

## [1.0.0] (2025-04-01)

* initial implementation of mod