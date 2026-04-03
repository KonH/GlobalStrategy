# Context for Parsing Historical GeoJSON (historical-basemaps, \~1880)

## 1. Dataset Overview

The **historical-basemaps** dataset provides political boundaries as
**GeoJSON FeatureCollections**.

For a given year (e.g., 1880), data typically represents: - Countries or
large political entities (not provinces)

Each file: - Encodes polygons/multipolygons for territories - Uses WGS84
geographic coordinates (EPSG:4326) - Represents the world in
longitude/latitude degrees

------------------------------------------------------------------------

## 2. GeoJSON Structure

### Top-level

``` json
{
  "type": "FeatureCollection",
  "features": [ ... ]
}
```

### Feature

``` json
{
  "type": "Feature",
  "properties": { ... },
  "geometry": {
    "type": "Polygon" | "MultiPolygon",
    "coordinates": [...]
  }
}
```

------------------------------------------------------------------------

## 3. Geometry Semantics

### Polygon

``` json
"coordinates": [
  [ [lon, lat], [lon, lat], ... ]
]
```

### Polygon with holes

``` json
"coordinates": [
  [ outer ring ],
  [ hole 1 ],
  [ hole 2 ]
]
```

### MultiPolygon

``` json
"coordinates": [
  [ [outer ring], [hole1], ... ],
  [ [outer ring], ... ]
]
```

### Key Points

-   Coordinates are always `[longitude, latitude]`
-   Rings are closed (first == last point)
-   Outer ring: usually counter-clockwise
-   Holes: usually clockwise
-   One country may contain multiple polygons

------------------------------------------------------------------------

## 4. Coordinate System & Projection

-   CRS: WGS84 (EPSG:4326)
-   Units: degrees

### Ranges

-   Longitude: \[-180, 180\]
-   Latitude: \[-90, 90\]

### Implications

-   Data is spherical, not planar
-   Requires projection into 2D Cartesian space
-   No elevation (Z axis)

------------------------------------------------------------------------

## 5. Properties (Metadata)

Typical fields: - Name - Temporal data - Optional IDs

### Notes

-   Naming is not standardized
-   Entities may include empires, colonies, protectorates

------------------------------------------------------------------------

## 6. Data Characteristics (circa 1880)

-   Reflects historical political borders
-   Colonial empires are fragmented
-   Some regions may be missing or simplified

### Topology

-   Borders may not align perfectly
-   Gaps or overlaps may exist

------------------------------------------------------------------------

## 7. Precision & Density

-   Coordinates: floating-point
-   Vertex count varies widely

### Implications

-   Not optimized for real-time rendering

------------------------------------------------------------------------

## 8. Common Edge Cases

Expect: - MultiPolygon countries - Disconnected territories - Holes -
Degenerate geometry - Antimeridian crossing - Inconsistent winding

------------------------------------------------------------------------

## 9. Logical Representation

-   One Feature = one entity
-   May map to multiple meshes

### Limitations

-   No provinces
-   No hierarchy
-   No adjacency graph

------------------------------------------------------------------------

## 10. Data Size & Performance

-   Several MB per dataset
-   Costs: parsing and traversal

------------------------------------------------------------------------

## 11. What Is NOT Included

-   Terrain
-   Economy
-   Provinces
-   Connectivity
-   Meshes

------------------------------------------------------------------------

## 12. Minimal Mental Model

-   Flat list of entities
-   Each entity = polygons
-   Each polygon = rings of points
