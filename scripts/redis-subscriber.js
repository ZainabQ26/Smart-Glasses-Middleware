// Node.js Redis subscriber example for background task completion
// Usage: node scripts/redis-subscriber.js
// Requires: npm install ioredis

const Redis = require('ioredis');
const url = process.env.REDIS_URL || 'redis://localhost:6379';
const channel = 'background:task:completed';

const sub = new Redis(url);
sub.subscribe(channel, (err, count) => {
  if (err) {
    console.error('Subscribe error', err);
    process.exit(1);
  }
  console.log(`Subscribed to ${channel}`);
});

sub.on('message', (ch, message) => {
  console.log(`Message on ${ch}: ${message}`);
  try {
    const payload = JSON.parse(message);
    console.log('Task', payload.id, 'status', payload.status);
  } catch (e) {
    console.log('Non-JSON payload:', message);
  }
});
