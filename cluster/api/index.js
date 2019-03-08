'use strict';

require('dotenv').config();
const io = require('socket.io')(80);
const redis = require('redis');
const Push = require('pushover-notifications');
let notify = null;

const redisClient = redis.createClient({
    host: process.env.REDIS_HOST,
    port: process.env.REDIS_PORT,
    password: process.env.REDIS_PASSWORD,
    db: 0
});

if (process.env.PUSHOVER_ENABLED == true) {
    console.log('Starting pushover...');

    notify = new Push({
        user: process.env.PUSHOVER_USER,
        token: process.env.PUSHOVER_TOKEN,
        onerror: (err) => {
            console.error('Push error', err);
        }
    });
}

redisClient.on('error', err => console.error('Error connecting to redis', err));
redisClient.on('ready', () => {
    console.log('Connected to redis')
});

const connectedSockets = [];

redisClient.on('message', (channel, message) => {
    console.log(channel, message)

    if (channel === 'alerts') {
        io.sockets.emit(channel, message)

        const alert = JSON.parse(message)

        if (alert.status == 'Error' && process.env.PUSHOVER_ENABLED == true) {
            notify.send({
                title: `Security Alert`,
                message: `${alert.name}: ${alert.text}`,
                sound: 'magic'
            })
        }
    }
});

redisClient.subscribe('alerts');


io.on('connection', (socket) => {
    console.log('user connected');

    connectedSockets.push(socket);
});
