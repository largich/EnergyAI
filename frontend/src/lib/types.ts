// Mirror of the C# DTOs in EnergyAi.Server/Dtos.
// Keep in sync when adding fields on either side.

export type Granularity = "Hour" | "Day" | "Week" | "Month";

export interface ConsumptionBucket {
  bucketStartUtc: string; // ISO-8601
  totalQuantity: number | null;
  sampleCount: number;
}

export interface ConsumptionSeries {
  equipmentCode: string;
  classCode: string;
  className: string;
  measurementCode: string;
  measurementName: string;
  unitSymbol: string;
  granularity: Granularity;
  buckets: ConsumptionBucket[];
}

export interface ComparisonResult {
  current: ConsumptionSeries;
  previous: ConsumptionSeries;
  currentTotal: number;
  previousTotal: number;
  deltaAbsolute: number;
  deltaPercent: number | null;
}

export interface AnomalyPoint {
  bucketStartUtc: string;
  equipmentCode: string;
  classCode: string;
  measurementCode: string;
  unitSymbol: string;
  quantity: number;
  expectedQuantity: number;
  deviationPercent: number;
  zScore: number;
  severity: "low" | "medium" | "high";
  reason: string;
}

export interface ClassCode {
  id: number;
  code: string;
  name: string | null;
}

export interface Equipment {
  code: string;
}

export interface Measurement {
  code: string;
  name: string;
  unitSymbol: string;
}

export interface HeatmapCell {
  dayOfWeek: number;  // 0 = Sunday … 6 = Saturday
  hour: number;       // 0 – 23
  avgQuantity: number;
  sampleCount: number;
}

export interface HeatmapSeries {
  equipmentCode: string;
  classCode: string;
  className: string;
  measurementCode: string;
  measurementName: string;
  unitSymbol: string;
  cells: HeatmapCell[];
}
