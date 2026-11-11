"""Concurrency smoke test. Run: python -m pip install requests && python tests/concurrency_test.py"""
import concurrent.futures, os, requests, uuid
BASE = os.getenv('BASE_URL', 'http://localhost:8080/api')
created = requests.post(f'{BASE}/classes', json={'name': f'Concurrency test {uuid.uuid4()}', 'totalSeats': 3}, timeout=10)
created.raise_for_status(); class_id = created.json()['id']
def apply(i):
    student = str(20000000000000000 + i)
    return requests.post(f'{BASE}/classes/{class_id}/holds', headers={'X-Student-Id': student}, timeout=20)
with concurrent.futures.ThreadPoolExecutor(max_workers=25) as pool:
    responses = list(pool.map(apply, range(25)))
accepted = [r for r in responses if r.status_code == 201]
conflicts = [r for r in responses if r.status_code == 409]
assert len(accepted) <= 3, f'Over-allocation detected: {len(accepted)} holds'
summary = requests.get(f'{BASE}/classes/{class_id}', timeout=10); summary.raise_for_status(); data = summary.json()
assert data['heldSeats'] + data['confirmedSeats'] <= data['totalSeats']
print({'accepted': len(accepted), 'conflicts': len(conflicts), 'class': data})
