import pathlib
import sys
import unittest

from shapely.geometry import Polygon, mapping

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from generate_provinces import add_topology_properties


def feature(province_id, points):
	return {
		"type": "Feature",
		"properties": {"provinceId": province_id},
		"geometry": mapping(Polygon(points)),
	}


class ProvinceTopologyTests(unittest.TestCase):
	def test_shared_segments_are_symmetric_but_point_contacts_are_not_neighbors(self):
		features = [
			feature("B", [(1, 0), (2, 0), (2, 1), (1, 1)]),
			feature("A", [(0, 0), (1, 0), (1, 1), (0, 1)]),
			feature("C", [(2, 1), (3, 1), (3, 2), (2, 2)]),
		]

		add_topology_properties(features)

		by_id = {item["properties"]["provinceId"]: item["properties"] for item in features}
		self.assertEqual(["B"], by_id["A"]["neighborProvinceIds"])
		self.assertEqual(["A"], by_id["B"]["neighborProvinceIds"])
		self.assertEqual([], by_id["C"]["neighborProvinceIds"])
		self.assertEqual(0.5, by_id["A"]["centroidX"])
		self.assertEqual(0.5, by_id["A"]["centroidY"])

	def test_neighbor_lists_have_stable_ordinal_order(self):
		features = [
			feature("Center", [(0, 0), (1, 0), (1, 2), (0, 2)]),
			feature("Zulu", [(1, 1), (2, 1), (2, 2), (1, 2)]),
			feature("Alpha", [(1, 0), (2, 0), (2, 1), (1, 1)]),
		]

		add_topology_properties(features)

		center = next(
			item["properties"] for item in features
			if item["properties"]["provinceId"] == "Center")
		self.assertEqual(["Alpha", "Zulu"], center["neighborProvinceIds"])


if __name__ == "__main__":
	unittest.main()
