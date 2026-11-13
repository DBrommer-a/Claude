# KestrelFPV

Die Flugphysik von Frostline (`Assets/Frostline/Flight/Core`) baut auf
[KestrelFPV](https://github.com/eleurent/KestrelFPV) von Edouard Leurent auf
(MIT-Lizenz, siehe `LICENSE.txt`).

Übernommen wurden der Aufbau des Modells: quadratisches Schub- und
Drehmomentmodell je Propeller, Summe der Kräfte über die Motorpositionen,
Mixer normiert auf die Stellwirkung, Ratenregler innen und Lageregler außen,
sowie die Werte der Parrot Bebop als Vergleichs-Preset
(`QuadConfig.KestrelBebop`).

Der Code selbst wurde für Unity 6 neu geschrieben und ist unabhängig von
Unity testbar. Neu sind Betaflight- und Actual-Rates, I-, D- und
Vorsteuer-Anteil, Airmode, Motorträgheit mit Drehmomentgrenze,
Akku-Spannungseinbruch, Schubverlust bei Fahrt, richtungsabhängiger
Luftwiderstand, Horizon- und Turtle-Modus und 500-Hz-Takt.

Aus Betaflight wurde kein Quelltext übernommen (GPL); die Rate-Kurven sind
nach ihren öffentlich dokumentierten Formeln nachgebaut.
