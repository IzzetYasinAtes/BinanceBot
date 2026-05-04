UPDATE Strategies SET ParametersJson = REPLACE(REPLACE(ParametersJson, '"BeMoveTriggerPct":0.002', '"BeMoveTriggerPct":0.001'), '"BeMoveOffsetPct":0.002', '"BeMoveOffsetPct":0.001') WHERE Type = 3;
