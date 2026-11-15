# QSO Scheduler

The QSO Scheduler panel helps you plan a satellite QSO with a DX station by finding the time windows when
the satellite is simultaneously visible from your location and from the DX location.

Click on **View / QSO Scheduler** to open the panel:

![QSO Scheduler panel](../images/qso_scheduler_panel.png)

## Selecting the Satellite and the DX Station

- **Satellite** - select the satellite for the QSO from the drop-down list. The list contains the satellites
  in the currently selected group, see [Creating Satellite Groups](creating_satellite_groups.md);
- **DX Grid Square** - enter the 4- or 6-character Maidenhead grid square of the DX station. The input
  box turns pink while the value is incomplete or invalid.

Your own grid square and altitude are taken from the user details in the
[Settings Window](settings_window.md). The DX station is assumed to be at sea level.

## Choose a Time Range and Start a Search

1. Set **Start (UTC)** and **End (UTC)** in the two date/time fields. Both
   are explicitly interpreted as **UTC**, regardless of the Windows system
   time zone. The defaults are the current UTC minute and 14 days later.
2. After selecting a satellite and entering a valid DX grid, press
   **Start calculation**. Entering or editing a grid **never runs an orbit
   prediction automatically**.
3. The progress bar reports the local-station pass calculation, DX-station
   pass calculation, then overlap/path calculations. The orbital propagation
   and sky-path sampling execute on a background worker, not on the WinForms
   rendering thread.
4. During a search, the button becomes **Cancel**. Cancelling, changing
   the satellite/grid/date range, or refreshing orbit/site information
   cancels the active search; the new inputs are not calculated until the
   operator explicitly starts another search.
5. On completion, the list is published all at once and the progress bar
   remains at 100% until the inputs change. If no common windows exist, the
   result count is zero. Windows shorter than 30 seconds are excluded.

Cancellation is checked between each pass-prediction stage and during overlap
sampling. A single native orbit-engine call already in progress may finish
before cancellation is observed, but the interface remains responsive and
its obsolete result is never published.

## Reading the Prediction List

Each item in the list represents one common visibility window within the **selected UTC time range**, sorted by start time:

- **Satellite name** is highlighted in light yellow for VHF downlinks and light cyan for UHF downlinks;
- **Orbit number** follows the satellite name;
- **Wait time** on the upper right shows how long until the window starts, or **Now** in green
  if the window is already in progress;
- **Start and end times** of the common segment are displayed in the time zone selected on the
  [Clock](clock.md) widget, marked with **Z** for UTC or **LT** for local time (the label shows
  **Geostationary** instead for geostationary satellites);
- **Duration** of the common segment and the maximum elevation of the satellite from each station during
  that segment;
- **Two mini sky-views** on the right show the satellite path during the common segment, as seen from
  your station (left) and from the DX station (right). The green dot marks the start of the segment,
  the red dot marks the end.

## Pass Details

Click on a list item to open the QSO Schedule window with more information about the pass:

![QSO Schedule window](../images/qso_schedule_window.png)

The window displays the full sky view for the pass from each station, with the entire pass arc drawn in
silver and the common segment highlighted in green. The green check mark indicates the AOS point, the
red X mark indicates the LOS point. The text above the charts lists the satellite, orbit number, common
segment in local and UTC time, duration, time until the segment starts, and the maximum elevation of the
satellite from each station during the common segment.
