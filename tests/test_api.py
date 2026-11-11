
import concurrent.futures, os, unittest, uuid
import requests
BASE = os.getenv('BASE_URL', 'http://localhost:8080/api')

class AdmissionApiTests(unittest.TestCase):
    def create_class(self, seats=5):
        r = requests.post(f'{BASE}/classes', json={'name': f'Automated test {uuid.uuid4()}', 'totalSeats': seats}, timeout=15)
        self.assertEqual(r.status_code, 201, r.text)
        return r.json()['id']

    def test_01_create_class_and_list_seat_counts(self):
        cid = self.create_class(7)
        r = requests.get(f'{BASE}/classes/{cid}', timeout=15)
        self.assertEqual(r.status_code, 200)
        self.assertEqual(r.json()['totalSeats'], 7)
        self.assertEqual(r.json()['availableSeats'], 7)

    def test_02_hold_and_confirm_is_idempotent(self):
        cid = self.create_class(2); sid = str(20152691234560001); headers = {'X-Student-Id': sid}
        hold = requests.post(f'{BASE}/classes/{cid}/holds', headers=headers, timeout=15)
        self.assertEqual(hold.status_code, 201, hold.text); hid = hold.json()['holdId']
        first = requests.post(f'{BASE}/holds/{hid}/confirm', headers=headers, timeout=15)
        second = requests.post(f'{BASE}/holds/{hid}/confirm', headers=headers, timeout=15)
        self.assertEqual(first.status_code, 200); self.assertEqual(second.status_code, 200)
        self.assertEqual(second.json()['status'], 'CONFIRMED')
        data = requests.get(f'{BASE}/classes/{cid}', timeout=15).json()
        self.assertEqual(data['confirmedSeats'], 1)

    def test_03_cancelled_hold_releases_seat(self):
        cid = self.create_class(1); h1 = {'X-Student-Id': str(20152691234560002)}
        created = requests.post(f'{BASE}/classes/{cid}/holds', headers=h1, timeout=15)
        self.assertEqual(created.status_code, 201, created.text)
        deleted = requests.delete(f"{BASE}/holds/{created.json()['holdId']}", headers=h1, timeout=15)
        self.assertEqual(deleted.status_code, 204)
        h2 = {'X-Student-Id': str(20152691234560003)}
        next_hold = requests.post(f'{BASE}/classes/{cid}/holds', headers=h2, timeout=15)
        self.assertEqual(next_hold.status_code, 201, next_hold.text)

    def test_04_student_cannot_apply_to_two_different_classes(self):
        first_class = self.create_class(2)
        second_class = self.create_class(2)
        headers = {'X-Student-Id': str(20152691234560004)}
        first = requests.post(f'{BASE}/classes/{first_class}/holds', headers=headers, timeout=15)
        second = requests.post(f'{BASE}/classes/{second_class}/holds', headers=headers, timeout=15)
        self.assertEqual(first.status_code, 201, first.text)
        self.assertEqual(second.status_code, 409, second.text)

    def test_05_concurrent_requests_never_overbook(self):
        capacity = 3; cid = self.create_class(capacity)
        def apply(i):
            return requests.post(f'{BASE}/classes/{cid}/holds', headers={'X-Student-Id': str(20152691234600000+i)}, timeout=30)
        with concurrent.futures.ThreadPoolExecutor(max_workers=25) as pool:
            results = list(pool.map(apply, range(25)))
        accepted = sum(r.status_code == 201 for r in results)
        self.assertLessEqual(accepted, capacity, f'{accepted} accepted for {capacity} seats')
        data = requests.get(f'{BASE}/classes/{cid}', timeout=15).json()
        self.assertLessEqual(data['heldSeats'] + data['confirmedSeats'], capacity)

    def test_06_same_student_concurrent_cross_class_requests_only_one_succeeds(self):
        class_ids = [self.create_class(2), self.create_class(2)]
        headers = {'X-Student-Id': str(20152691234560005)}
        def apply(cid):
            return requests.post(f'{BASE}/classes/{cid}/holds', headers=headers, timeout=30)
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(apply, class_ids))
        accepted = sum(r.status_code == 201 for r in results)
        self.assertEqual(accepted, 1, [r.status_code for r in results])

if __name__ == '__main__': unittest.main()
