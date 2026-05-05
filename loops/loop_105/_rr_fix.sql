UPDATE Strategies SET ParametersJson = REPLACE(ParametersJson, '"TpRiskRewardRatio":2.0', '"TpRiskRewardRatio":1.0') WHERE Type = 3;
